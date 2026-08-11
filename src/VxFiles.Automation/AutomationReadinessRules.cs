// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Answers whether an Automation Action's external tools are configured and still where they were left, so a
/// host surface can say so before a run rather than after one fails.
/// </summary>
/// <remarks>
/// Per action rather than per package, because a package's tools are declared once and referenced individually:
/// configuring FFmpeg but not FFprobe leaves every action that only needs FFmpeg perfectly runnable, and a
/// package-wide verdict would take them all down with the one that is not.
///
/// <para>
/// A state lookup and <see cref="AutomationExternalToolPathRules"/>, and deliberately nothing else. This runs on
/// every snapshot projection — session start, every catalog refresh, every applied configuration — so it is held
/// to two stat calls per tool. The SHA-256 and the declared version floor belong to
/// <see cref="AutomationDependencyResolver"/> and the run path alone; a tool this says is ready can still be
/// refused there, which is the price of it being cheap enough to ask this often.
/// </para>
/// </remarks>
internal static class AutomationReadinessRules
{
	/// <summary>
	/// Returns one diagnostic per external tool this action needs and cannot use, or empty when it can use them all.
	/// </summary>
	/// <remarks>
	/// The tool is named rather than its path, because the path is exactly what the user has to supply or correct
	/// and quoting one that leads nowhere back at them explains nothing they do not already have on screen.
	/// </remarks>
	public static ImmutableArray<string> UnusableTools(
		AutomationPackageDefinition package,
		AutomationActionDefinition action,
		AutomationPackageState state)
	{
		if (action.ExternalToolIds.IsEmpty)
			return [];

		var diagnostics = ImmutableArray.CreateBuilder<string>();
		foreach (var toolId in action.ExternalToolIds)
		{
			// A reference no declaration answers is refused when the manifest is read, so this cannot miss.
			var definition = package.ExternalTools.First(tool => string.Equals(tool.Id, toolId, StringComparison.Ordinal));
			if (!state.ExternalTools.TryGetValue(toolId, out var configuration))
			{
				diagnostics.Add($"'{definition.DisplayName}' has not been configured yet.");
				continue;
			}

			// Configured is not the same as present: an uninstall or an upgrade that moved a program leaves the
			// path behind it, and the user learns that here rather than from a run that starts and cannot.
			if (AutomationExternalToolPathRules.Evaluate(configuration.ExecutablePath).Verdict is not AutomationToolPathVerdict.Valid)
				diagnostics.Add($"'{definition.DisplayName}' is configured with a path that cannot be used.");
		}

		return diagnostics.ToImmutable();
	}

	/// <summary>
	/// Aggregates its actions' readiness into the package's own, leaving any verdict discovery already reached
	/// untouched.
	/// </summary>
	/// <remarks>
	/// A package that failed validation keeps <see cref="AutomationAvailability.Disabled"/>: nothing is
	/// configurable about a package that could not be read, and offering to configure it would be an invitation
	/// to nowhere. An action that failed validation likewise does not drag its package down — that is already
	/// reported as a count of how many actions survived, and has been since before readiness existed.
	/// </remarks>
	public static AutomationAvailability ForPackage(
		AutomationAvailability discovered,
		ImmutableArray<AutomationActionSnapshot> actions)
		=> discovered is AutomationAvailability.Available &&
			actions.Any(action => action.Availability is AutomationAvailability.NeedsConfiguration)
			? AutomationAvailability.NeedsConfiguration
			: discovered;
}
