// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal sealed record ResolvedAutomationDependencies(
	ImmutableDictionary<string, AutomationSettingValue> Settings,
	ImmutableArray<AutomationExternalToolIdentity> ExternalTools);

/// <summary>
/// A declared external tool is absent or unusable, so the package needs configuration before any run.
/// </summary>
internal sealed class AutomationMissingDependencyException(string message) : InvalidOperationException(message);

/// <summary>
/// Binds an action's declared settings and its package's external tools to the persisted configuration,
/// producing the concrete identities transported to the action and mixed into the trust fingerprint.
/// </summary>
internal static class AutomationDependencyResolver
{
	public static async ValueTask<ResolvedAutomationDependencies> ResolveAsync(
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationPackageState packageState,
		AutomationActionSettings actionSettings)
	{
		var settings = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
		foreach (var definition in action.Settings)
		{
			var value = AutomationSettingRules.Current(actionSettings.Values, definition.Key, definition.Type, definition.DefaultValue);
			if (!AutomationSettingRules.TryResolve(definition, value, out var resolved))
				throw new InvalidOperationException($"Configured setting '{definition.Key}' is invalid; configure the action again.");
			settings.Add(definition.Key, resolved);
		}

		var tools = ImmutableArray.CreateBuilder<AutomationExternalToolIdentity>();
		foreach (var toolId in action.ExternalToolIds)
		{
			// A reference no declaration answers is refused when the manifest is read, so this cannot miss.
			var definition = package.FindExternalTool(toolId)!;
			tools.Add(await ResolveExternalToolAsync(definition, packageState));
		}

		return new(settings.ToImmutable(), tools.ToImmutable());
	}

	private static async ValueTask<AutomationExternalToolIdentity> ResolveExternalToolAsync(
		AutomationExternalToolDefinition definition,
		AutomationPackageState packageState)
	{
		if (!packageState.ExternalTools.TryGetValue(definition.Id, out var configuration))
			throw new AutomationMissingDependencyException($"Configure the required external tool '{definition.DisplayName}'.");

		var path = RequireUsablePath(definition.DisplayName, configuration.ExecutablePath);
		var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
		if (definition.MinimumFileVersion is not null)
		{
			// Fails closed on both sides. FFmpeg's own Windows builds carry no version resource at all, and
			// reading that silence as a pass handed the manifest author a guarantee nothing ever enforced. The
			// declared floor is never parsed when the manifest is read, so it can be unusable in the same way.
			if (!Version.TryParse(definition.MinimumFileVersion, out var minimum))
				throw new AutomationMissingDependencyException($"'{definition.DisplayName}' cannot be used: its package requires version '{definition.MinimumFileVersion}', which is not a version number.");
			if (!Version.TryParse(version, out var actual))
				throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' with a build that reports its file version; version {definition.MinimumFileVersion} or later is required.");
			if (actual < minimum)
				throw new AutomationMissingDependencyException($"Configure '{definition.DisplayName}' version {definition.MinimumFileVersion} or later.");
		}

		return new(
			definition.Id,
			path,
			$"sha256:{await AutomationFileHash.ComputeHexAsync(path)}",
			version);
	}

	/// <summary>
	/// Returns what a configured path leads to, or refuses it with the reason it cannot be run.
	/// </summary>
	/// <remarks>
	/// Shared by the run path and by applying a configuration, so a path a run would refuse is never stored. It
	/// holds a link's target rather than the link, resolved on every run: winget and scoop put their executables
	/// behind a shim, which is how most people have FFmpeg, and resolving per run is what keeps such an install
	/// configured across an upgrade that re-points the shim. Package and runtime trees are held to the opposite
	/// rule — see <see cref="AutomationTrustFingerprint"/> — because those are content VxFiles hashes wholesale,
	/// not one file the user chose.
	/// </remarks>
	public static string RequireUsablePath(string displayName, string executablePath)
	{
		var evaluation = AutomationExternalToolPathRules.Evaluate(executablePath);
		return evaluation.Verdict switch
		{
			AutomationToolPathVerdict.Valid => evaluation.EffectivePath,
			AutomationToolPathVerdict.NotAbsolute or AutomationToolPathVerdict.Malformed or
				AutomationToolPathVerdict.NotAnExecutable or AutomationToolPathVerdict.NotFound
				=> throw new AutomationMissingDependencyException(
					$"Configure '{displayName}' with an absolute ordinary executable path."),
			AutomationToolPathVerdict.Unresolvable => throw new AutomationMissingDependencyException(
				$"Configure '{displayName}' with an executable path that resolves."),
			AutomationToolPathVerdict.TargetNotFound => throw new AutomationMissingDependencyException(
				$"Configure '{displayName}': its path leads to '{evaluation.EffectivePath}', which is not there. Reinstall it or configure the new location."),
			AutomationToolPathVerdict.TargetNotAnExecutable => throw new AutomationMissingDependencyException(
				$"Configure '{displayName}' with a path that leads to a program; this one leads to '{Path.GetFileName(evaluation.EffectivePath)}'."),
			_ => throw new AutomationMissingDependencyException(
				$"Configure '{displayName}': its path cannot be used."),
		};
	}
}
