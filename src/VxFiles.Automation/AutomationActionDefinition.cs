// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

internal enum AutomationInputMode
{
	JsonStdin,
	ArgvPaths,
}

internal enum AutomationOutputProtocol
{
	NdjsonV1,
	ExitCode,
}

internal sealed record AutomationExternalToolDefinition(
	string Id,
	string DisplayName,
	string? MinimumFileVersion);

internal sealed record AutomationSettingDefinition(
	string Key,
	string DisplayName,
	string Description,
	AutomationSettingType Type,
	AutomationSettingValue DefaultValue,
	double? Minimum,
	double? Maximum,
	int? MinimumLength,
	int? MaximumLength,
	ImmutableArray<string> Values);

/// <summary>
/// Everything the runner needs to execute one Automation Action.
/// </summary>
internal sealed record AutomationActionDefinition(
	AutomationActionId Id,
	string EntryPointPath,
	AutomationInputMode InputMode,
	AutomationSelectionPolicy Selection,
	int TimeoutSeconds,
	int MaxOutputBytes,
	AutomationOutputProtocol OutputProtocol,
	ImmutableArray<string> ExternalToolIds,
	ImmutableArray<AutomationSettingDefinition> Settings);

/// <summary>
/// The trust, update, and validation unit that owns runnable Automation Actions.
/// </summary>
internal sealed record AutomationPackageDefinition(
	AutomationPackageId Id,
	string PackagePath,
	string PackageVersion,
	string DisplayName,
	byte[] ManifestBytes,
	ImmutableArray<AutomationExternalToolDefinition> ExternalTools,
	ImmutableDictionary<AutomationActionLocalId, AutomationActionDefinition> Actions)
{
	/// <summary>
	/// Finds a declared external tool by the id an action or a stored configuration refers to it by.
	/// </summary>
	/// <remarks>
	/// One lookup for the three that need it — resolving a run, screening a submitted configuration, and
	/// composing readiness — so the ordinal comparison that decides whether two ids are the same tool is stated
	/// once. Returns <see langword="null"/> rather than throwing, because only one of the three is answering for
	/// an id a user supplied; the other two hold an id the manifest reader already checked against this list.
	/// </remarks>
	public AutomationExternalToolDefinition? FindExternalTool(string id)
		=> ExternalTools.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.Ordinal));
}
