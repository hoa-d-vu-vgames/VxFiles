// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace VxFiles.Automation.Abstractions;

public readonly record struct AutomationPackageId
{
	private static readonly Regex ValuePattern = new(
		@"^(?=.{3,128}$)[a-z0-9]+(?:[.-][a-z0-9]+)+$",
		RegexOptions.CultureInvariant);

	private readonly string? value;

	public string Value => value ?? throw new InvalidOperationException("The package id is not initialized.");

	private AutomationPackageId(string value)
		=> this.value = value;

	public static AutomationPackageId Parse(string value)
		=> TryParse(value, out var result)
			? result
			: throw new ArgumentException("Package id must be publisher-qualified lowercase text between 3 and 128 characters.", nameof(value));

	public static bool TryParse(string? value, out AutomationPackageId result)
	{
		if (value is not null && ValuePattern.IsMatch(value))
		{
			result = new(value);
			return true;
		}

		result = default;
		return false;
	}

	public override string ToString() => Value;
}

public readonly record struct AutomationActionLocalId
{
	private static readonly Regex ValuePattern = new(
		@"^[a-z][a-z0-9-]{0,63}$",
		RegexOptions.CultureInvariant);

	private readonly string? value;

	public string Value => value ?? throw new InvalidOperationException("The action id is not initialized.");

	private AutomationActionLocalId(string value)
		=> this.value = value;

	public static AutomationActionLocalId Parse(string value)
		=> TryParse(value, out var result)
			? result
			: throw new ArgumentException("Action id must be lowercase text between 1 and 64 characters.", nameof(value));

	public static bool TryParse(string? value, out AutomationActionLocalId result)
	{
		if (value is not null && ValuePattern.IsMatch(value))
		{
			result = new(value);
			return true;
		}

		result = default;
		return false;
	}

	public override string ToString() => Value;
}

public readonly record struct AutomationActionId
{
	public AutomationPackageId PackageId { get; }
	public AutomationActionLocalId LocalId { get; }

	public string Value => $"{PackageId.Value}/{LocalId.Value}";

	public AutomationActionId(AutomationPackageId packageId, AutomationActionLocalId localId)
	{
		_ = packageId.Value;
		_ = localId.Value;
		PackageId = packageId;
		LocalId = localId;
	}

	public override string ToString() => Value;
}

/// <summary>
/// Whether an Automation Package or Action can be run, and what stands in the way when it cannot.
/// </summary>
/// <remarks>
/// <see cref="NeedsConfiguration"/> is not a fault. A package that declares an external tool starts here on
/// every clean install, because VxFiles neither installs nor locates such a tool — the user points at it. It is
/// separate from <see cref="MissingDependency"/> for that reason: one is the first thing a bundled package has
/// to say for itself, the other is something that went wrong.
///
/// <para>
/// It is also the only member that moves on its own. The other three are decided when a manifest is read, while
/// this one depends on what is configured and on what that configuration still points at, so it is recomposed
/// every time the snapshot is projected rather than recorded once.
/// </para>
/// </remarks>
public enum AutomationAvailability
{
	Available,
	Disabled,
	MissingDependency,
	NeedsConfiguration,
}

/// <summary>
/// What an Automation Action accepts as input: how many items, of which kinds, with which file extensions.
/// </summary>
/// <remarks>
/// This is a contract rather than a runner detail because a host surface has to answer "can this run on what
/// is selected right now?" before invoking anything. Evaluating it lives in <see cref="AutomationSelectionRules"/>
/// so the host and the session reach the same verdict.
/// </remarks>
public sealed record AutomationSelectionPolicy(
	int MinItems,
	int MaxItems,
	ImmutableArray<string> ItemKinds,
	ImmutableArray<string> Extensions);

/// <summary>
/// What kind of value a setting declares, and therefore which control edits it.
/// </summary>
/// <remarks>
/// Distinct from <see cref="AutomationSettingValueKind"/>, which has four members because <c>Enum</c>,
/// <c>FilePath</c> and <c>FolderPath</c> all <em>store</em> as a string. What a setting is and how it is held
/// are different facts; a host surface needs this one to choose between a text box, a picker and a dropdown.
/// </remarks>
public enum AutomationSettingType
{
	Boolean,
	Integer,
	Number,
	String,
	Enum,
	FilePath,
	FolderPath,
}

/// <summary>
/// One declared setting of an Automation Action, with everything needed to render and bind an editor for it.
/// </summary>
/// <remarks>
/// <paramref name="CurrentValue"/> is the stored value when the action has one and <paramref name="DefaultValue"/>
/// otherwise — the same fallback a run resolves settings by, so what a host shows is what a run would resolve.
/// Carrying it here is what lets an editor open without an asynchronous read, which is what makes Cancel free
/// rather than a rollback. A stored value that no longer satisfies the declaration below is still reported as
/// current: the run refuses it, and showing it is what gives the user something to correct.
///
/// <para>
/// The bounds are per type and mostly absent: <paramref name="Minimum"/> and <paramref name="Maximum"/> apply to
/// <c>Integer</c> and <c>Number</c>, the lengths to the string-backed types, and <paramref name="Values"/> is
/// non-empty only for <c>Enum</c>.
/// </para>
/// </remarks>
public sealed record AutomationSettingSchema(
	string Key,
	string DisplayName,
	string Description,
	AutomationSettingType Type,
	AutomationSettingValue DefaultValue,
	AutomationSettingValue CurrentValue,
	double? Minimum,
	double? Maximum,
	int? MinimumLength,
	int? MaximumLength,
	ImmutableArray<string> Values);

/// <summary>
/// One Automation Action as a host surface sees it.
/// </summary>
/// <remarks>
/// <paramref name="Selection"/> is <see langword="null"/> for an action that failed validation: its manifest
/// never produced a policy, and it cannot be run regardless. Such an action carries no <paramref name="Settings"/>
/// either, for the same reason — nothing was validated to declare them.
/// </remarks>
public sealed record AutomationActionSnapshot(
	AutomationActionId Id,
	string DisplayName,
	string Description,
	string? Icon,
	AutomationAvailability Availability,
	ImmutableArray<string> Diagnostics,
	ImmutableArray<AutomationSettingSchema> Settings,
	AutomationSelectionPolicy? Selection = null);

public sealed record AutomationPackageSnapshot(
	AutomationPackageId Id,
	string PackageVersion,
	string DisplayName,
	string Description,
	string Author,
	string? Icon,
	AutomationAvailability Availability,
	ImmutableArray<string> Diagnostics,
	ImmutableArray<AutomationActionSnapshot> Actions);

public sealed record AutomationCatalogSnapshot(
	ImmutableArray<AutomationPackageSnapshot> Packages);
