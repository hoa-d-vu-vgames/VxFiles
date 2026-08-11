// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Holds a configuration a user submitted against what the package's manifest declares, so that only a
/// configuration a run could actually use is ever stored.
/// </summary>
/// <remarks>
/// Separate from <see cref="AutomationDependencyResolver"/> because the two face opposite ways: the resolver
/// binds what is already stored to a run about to start, and this screens what has not been stored yet. They
/// share the rules underneath — <see cref="AutomationDependencyResolver.RequireUsablePath"/> and
/// <see cref="AutomationSettingRules.TryResolve"/> — which is what keeps a value accepted here from being
/// refused later.
/// </remarks>
internal static class AutomationConfigurationRules
{
	/// <summary>
	/// Applies submitted external tools over the stored ones: a submitted path replaces, a submitted empty path
	/// clears, and a tool the submission does not mention keeps what it had.
	/// </summary>
	/// <remarks>
	/// Per entry rather than wholesale, because clearing a tool has one spelling — submitting it empty — and a
	/// dialog that saves only an action's settings must not silently unconfigure the package's tools by omission.
	///
	/// <para>
	/// The configured spelling is kept rather than the resolved target: a shim is resolved on every run, so
	/// storing what it pointed at today would unconfigure the tool the next time its installer re-pointed it.
	/// Entries are keyed by the declared id, so a submitted record disagreeing with its own key cannot produce a
	/// configuration that reads back differently than it was applied.
	/// </para>
	/// </remarks>
	public static ImmutableDictionary<string, AutomationExternalToolConfiguration> ScreenExternalTools(
		AutomationPackageDefinition package,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> stored,
		ImmutableDictionary<string, AutomationExternalToolConfiguration> submitted)
	{
		var accepted = stored;
		foreach (var (id, configuration) in submitted)
		{
			var definition = package.FindExternalTool(id)
				?? throw new InvalidOperationException($"Automation Package '{package.Id.Value}' declares no external tool '{id}'.");
			if (string.IsNullOrWhiteSpace(configuration.ExecutablePath))
			{
				accepted = accepted.Remove(id);
				continue;
			}

			AutomationDependencyResolver.RequireUsablePath(definition.DisplayName, configuration.ExecutablePath);
			accepted = accepted.SetItem(id, new(id, configuration.ExecutablePath));
		}

		return accepted;
	}

	/// <summary>
	/// Accepts submitted settings only for actions this package has, keys those actions declare, and values those
	/// declarations admit.
	/// </summary>
	public static ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> ScreenActionSettings(
		AutomationPackageDefinition package,
		ImmutableDictionary<AutomationActionLocalId, AutomationActionSettings> submitted)
	{
		var accepted = ImmutableDictionary.CreateBuilder<AutomationActionLocalId, AutomationActionSettings>();
		foreach (var (localId, settings) in submitted)
		{
			if (!package.Actions.TryGetValue(localId, out var action))
				throw new InvalidOperationException($"Automation Package '{package.Id.Value}' declares no action '{localId.Value}'.");

			var values = ImmutableDictionary.CreateBuilder<string, AutomationSettingValue>(StringComparer.Ordinal);
			foreach (var (key, value) in settings.Values)
			{
				var definition = action.Settings.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal))
					?? throw new InvalidOperationException($"Automation Action '{action.Id.Value}' declares no setting '{key}'.");
				if (!AutomationSettingRules.TryResolve(definition, value, out var resolved))
					throw new InvalidOperationException($"Setting '{definition.DisplayName}' was given a value it does not accept.");
				values.Add(key, resolved);
			}

			accepted.Add(localId, new(values.ToImmutable()));
		}

		return accepted.ToImmutable();
	}
}
