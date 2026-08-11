// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Identity and display text gathered while a manifest is read, so a failure part way through still
/// produces the most informative snapshot available.
/// </summary>
internal sealed class AutomationPackageMetadata(AutomationPackageId id, string displayName)
{
	public AutomationPackageId Id { get; set; } = id;
	public string PackageVersion { get; set; } = string.Empty;
	public string DisplayName { get; set; } = displayName;
	public string Description { get; set; } = string.Empty;
	public string Author { get; set; } = string.Empty;
	public string? Icon { get; set; }
	public bool HasValidIdentity { get; set; }
}

internal sealed class AutomationActionMetadata(AutomationActionLocalId localId)
{
	public AutomationActionLocalId LocalId { get; set; } = localId;
	public string DisplayName { get; set; } = "Invalid action";
	public string Description { get; set; } = string.Empty;
	public string? Icon { get; set; }
}

/// <summary>
/// Turns validation outcomes into the immutable snapshots host surfaces consume.
/// </summary>
internal static class AutomationSnapshotMapping
{
	public static AutomationPackageSnapshot DisabledPackage(AutomationPackageMetadata metadata, string diagnostic)
		=> new(
			metadata.Id,
			metadata.PackageVersion,
			metadata.DisplayName,
			string.IsNullOrEmpty(metadata.Description) ? diagnostic : metadata.Description,
			metadata.Author,
			metadata.Icon,
			AutomationAvailability.Disabled,
			[diagnostic],
			[]);

	public static AutomationPackageSnapshot AvailablePackage(
		AutomationPackageMetadata metadata,
		ImmutableArray<AutomationActionSnapshot> actions)
		=> new(
			metadata.Id,
			metadata.PackageVersion,
			metadata.DisplayName,
			metadata.Description,
			metadata.Author,
			metadata.Icon,
			AutomationAvailability.Available,
			[],
			actions);

	public static AutomationActionSnapshot DisabledAction(
		AutomationPackageId packageId,
		AutomationActionMetadata metadata,
		string diagnostic)
		=> new(
			new(packageId, metadata.LocalId),
			metadata.DisplayName,
			string.IsNullOrEmpty(metadata.Description) ? diagnostic : metadata.Description,
			metadata.Icon,
			AutomationAvailability.Disabled,
			[diagnostic],
			[]);

	public static AutomationActionSnapshot AvailableAction(
		AutomationPackageId packageId,
		AutomationActionMetadata metadata,
		AutomationSelectionPolicy selection,
		ImmutableArray<AutomationSettingDefinition> settings)
		=> new(
			new(packageId, metadata.LocalId),
			metadata.DisplayName,
			metadata.Description,
			metadata.Icon,
			AutomationAvailability.Available,
			[],
			[.. settings.Select(Schema)],
			selection);

	/// <summary>
	/// Completes a discovered catalog from what the user has stored: each action's settings applied over the
	/// declared defaults, and each action's readiness composed from the external tools it names.
	/// </summary>
	/// <remarks>
	/// Discovery reads manifests and never touches user state, so the two halves of a projection meet here. A
	/// caller has to apply this every time it takes a freshly discovered catalog — a catalog refresh rebuilds
	/// from manifests, which know nothing about what a user configured.
	///
	/// <para>
	/// Both halves live in the one pass because both are read from the same store at the same moment, and because
	/// a caller that applied one and forgot the other would publish a snapshot that is half a lie. Readiness in
	/// particular is composed here <em>every</em> time rather than recorded once: a verdict stored on a snapshot
	/// is erased the next time the catalog watcher fires, whereas one recomputed from the store cannot go stale.
	/// </para>
	///
	/// <para>
	/// Nothing is read for a package that declares neither settings nor external tools, so a catalog of packages
	/// like the bundled tracer costs no state I/O at all.
	/// </para>
	/// </remarks>
	public static async Task<ImmutableArray<AutomationPackageSnapshot>> WithStoredStateAsync(
		IAutomationStateStore stateStore,
		AutomationCatalog catalog,
		ImmutableArray<AutomationPackageSnapshot> packages,
		CancellationToken cancellationToken)
	{
		var updatedPackages = packages.ToBuilder();
		for (var packageIndex = 0; packageIndex < updatedPackages.Count; packageIndex++)
		{
			var package = updatedPackages[packageIndex];

			// A package that failed validation is in the snapshot to be explained, not run: it has no definition
			// behind it, so there is neither a setting to overlay nor a tool to look for.
			if (!catalog.Packages.TryGetValue(package.Id, out var definition))
				continue;
			if (definition.ExternalTools.IsEmpty && package.Actions.All(action => action.Settings.IsEmpty))
				continue;

			// One read for the whole package, because external tools are declared and configured package-wide
			// however many of its actions reference them.
			var state = definition.ExternalTools.IsEmpty
				? null
				: await stateStore.ReadPackageStateAsync(package.Id, cancellationToken);

			var updatedActions = package.Actions.ToBuilder();
			for (var actionIndex = 0; actionIndex < updatedActions.Count; actionIndex++)
			{
				var action = updatedActions[actionIndex];
				if (!action.Settings.IsEmpty)
				{
					var stored = (await stateStore.ReadActionSettingsAsync(action.Id, cancellationToken)).Values;
					if (!stored.IsEmpty)
					{
						action = action with
						{
							Settings = [.. action.Settings.Select(setting => setting with
							{
								CurrentValue = AutomationSettingRules.Current(stored, setting.Key, setting.Type, setting.DefaultValue),
							})],
						};
					}
				}

				updatedActions[actionIndex] = state is null
					? action
					: WithReadiness(definition, action, state);
			}

			var actions = updatedActions.ToImmutable();
			updatedPackages[packageIndex] = package with
			{
				Actions = actions,
				Availability = AutomationReadinessRules.ForPackage(package.Availability, actions),
			};
		}

		return updatedPackages.ToImmutable();
	}

	/// <summary>
	/// Marks an action that cannot reach one of its external tools, leaving one that failed validation alone.
	/// </summary>
	/// <remarks>
	/// Only an action discovery found <see cref="AutomationAvailability.Available"/> is considered. A disabled
	/// action has no definition to read tool references from, and telling the user to configure something for an
	/// action whose manifest is broken would send them after a fault that is not theirs.
	/// </remarks>
	private static AutomationActionSnapshot WithReadiness(
		AutomationPackageDefinition package,
		AutomationActionSnapshot action,
		AutomationPackageState state)
	{
		if (action.Availability is not AutomationAvailability.Available ||
			!package.Actions.TryGetValue(action.Id.LocalId, out var definition))
		{
			return action;
		}

		var unusable = AutomationReadinessRules.UnusableTools(package, definition, state);
		return unusable.IsEmpty
			? action
			: action with { Availability = AutomationAvailability.NeedsConfiguration, Diagnostics = unusable };
	}

	/// <summary>
	/// Projects a declared setting with no stored value applied: discovery reads manifests, not user state, so
	/// <c>CurrentValue</c> starts as the default until <see cref="WithStoredStateAsync"/> completes it.
	/// </summary>
	private static AutomationSettingSchema Schema(AutomationSettingDefinition definition)
		=> new(
			definition.Key,
			definition.DisplayName,
			definition.Description,
			definition.Type,
			definition.DefaultValue,
			definition.DefaultValue,
			definition.Minimum,
			definition.Maximum,
			definition.MinimumLength,
			definition.MaximumLength,
			definition.Values);

	/// <summary>
	/// Gives an action with an unusable id a content-stable identity, so diagnostics survive manifest reordering.
	/// </summary>
	public static AutomationActionLocalId FallbackActionId(JsonElement action)
	{
		var content = Encoding.UTF8.GetBytes(action.GetRawText());
		var hash = Convert.ToHexStringLower(SHA256.HashData(content));
		return AutomationActionLocalId.Parse($"invalid-{hash[..16]}");
	}
}
