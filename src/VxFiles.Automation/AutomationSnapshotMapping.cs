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
	/// Applies each action's stored settings over the defaults discovery left in place, completing the snapshot.
	/// </summary>
	/// <remarks>
	/// Discovery reads manifests and never touches user state, so the two halves of a settings projection meet
	/// here. A caller has to apply this every time it takes a freshly discovered catalog — a catalog refresh
	/// rebuilds from manifests, which know nothing about what a user configured.
	///
	/// <para>
	/// Only actions that declare settings are read for, so a catalog of packages like the bundled tracer — which
	/// declares none — costs no state I/O at all.
	/// </para>
	/// </remarks>
	public static async Task<ImmutableArray<AutomationPackageSnapshot>> WithStoredSettingsAsync(
		IAutomationStateStore stateStore,
		ImmutableArray<AutomationPackageSnapshot> packages,
		CancellationToken cancellationToken)
	{
		var updatedPackages = packages.ToBuilder();
		for (var packageIndex = 0; packageIndex < updatedPackages.Count; packageIndex++)
		{
			var package = updatedPackages[packageIndex];
			if (package.Actions.All(action => action.Settings.IsEmpty))
				continue;

			var updatedActions = package.Actions.ToBuilder();
			for (var actionIndex = 0; actionIndex < updatedActions.Count; actionIndex++)
			{
				var action = updatedActions[actionIndex];
				if (action.Settings.IsEmpty)
					continue;

				var stored = (await stateStore.ReadActionSettingsAsync(action.Id, cancellationToken)).Values;
				if (stored.IsEmpty)
					continue;

				updatedActions[actionIndex] = action with
				{
					Settings = [.. action.Settings.Select(setting => setting with
					{
						CurrentValue = AutomationSettingRules.Current(stored, setting.Key, setting.DefaultValue),
					})],
				};
			}

			updatedPackages[packageIndex] = package with { Actions = updatedActions.ToImmutable() };
		}

		return updatedPackages.ToImmutable();
	}

	/// <summary>
	/// Projects a declared setting with no stored value applied: discovery reads manifests, not user state, so
	/// <c>CurrentValue</c> starts as the default until <see cref="WithStoredSettingsAsync"/> completes it.
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
