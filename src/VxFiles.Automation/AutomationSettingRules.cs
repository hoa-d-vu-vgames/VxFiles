// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Typed action settings declared by a manifest.
/// </summary>
internal static partial class AutomationSettingRules
{
	private const AutomationValidationScope Scope = AutomationValidationScope.Action;

	private static readonly string[] SettingProperties =
	[
		"key",
		"displayName",
		"description",
		"type",
		"default",
		"minimum",
		"maximum",
		"minimumLength",
		"maximumLength",
		"values",
	];

	/// <summary>
	/// The value an action currently has for a declared setting: what is stored for it, or the manifest's default
	/// when nothing is.
	/// </summary>
	/// <remarks>
	/// Named rather than repeated, because a run and a host surface have to answer this the same way: what is
	/// displayed as current is what a run would resolve. A stored value that no longer satisfies its declaration
	/// is returned unchanged — <see cref="AutomationDependencyResolver"/> refuses the run over it, which is the
	/// only thing that tells the user there is something to correct.
	/// </remarks>
	public static AutomationSettingValue Current(
		ImmutableDictionary<string, AutomationSettingValue> stored,
		string key,
		AutomationSettingType type,
		AutomationSettingValue defaultValue)
		=> AsDeclared(type, stored.GetValueOrDefault(key, defaultValue));

	/// <summary>
	/// The value in the spelling its declared type calls for.
	/// </summary>
	/// <remarks>
	/// JSON has a single number type, so a whole number stored for a <c>number</c> setting is read back as an
	/// integer however it was written. A run and a host surface both have to see it as the number it was declared
	/// to be — otherwise a number setting could never hold 2, and an editor would be chosen by the wrong kind.
	/// </remarks>
	private static AutomationSettingValue AsDeclared(AutomationSettingType type, AutomationSettingValue value)
		=> type is AutomationSettingType.Number && value.Kind is AutomationSettingValueKind.Integer
			? new(AutomationSettingValueKind.Number, NumberValue: value.IntegerValue)
			: value;

	/// <summary>
	/// Accepts a value for a declared setting, in the spelling that declaration calls for.
	/// </summary>
	/// <remarks>
	/// One rule for two callers: a run resolves stored settings through it, and applying a configuration screens
	/// submitted ones through it, so a value a run would refuse can never be stored in the first place.
	///
	/// <para>
	/// A <c>number</c> declaration accepts an integer-kinded value, for the reason <see cref="AsDeclared"/> gives.
	/// </para>
	/// </remarks>
	public static bool TryResolve(
		AutomationSettingDefinition definition,
		AutomationSettingValue value,
		out AutomationSettingValue resolved)
	{
		return AutomationSettingValueRules.TryResolve(
			definition.Type,
			value,
			definition.Minimum,
			definition.Maximum,
			definition.MinimumLength,
			definition.MaximumLength,
			definition.Values,
			out resolved);
	}

	public static ImmutableArray<AutomationSettingDefinition> Validate(JsonElement action)
	{
		if (!action.TryGetProperty("settings", out var settings))
			return [];
		if (settings.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Action("settings must be an array.");

		var definitions = ImmutableArray.CreateBuilder<AutomationSettingDefinition>();
		var keys = new HashSet<string>(StringComparer.Ordinal);
		foreach (var item in settings.EnumerateArray())
		{
			var setting = AutomationManifestReader.RequireObject(item, "settings item", Scope);
			AutomationManifestReader.ValidateProperties(setting, SettingProperties, "settings item", Scope);
			var key = AutomationManifestReader.RequireString(setting, "key", Scope);
			if (!SettingKeyRegex().IsMatch(key) || !keys.Add(key))
				throw AutomationValidationException.Action($"Setting key '{key}' is invalid or duplicated.");
			var displayName = AutomationManifestReader.RequireBoundedString(setting, "displayName", 1, 80, Scope);
			var description = AutomationManifestReader.RequireBoundedString(setting, "description", 1, 500, Scope);
			var type = ReadType(key, AutomationManifestReader.RequireString(setting, "type", Scope));
			var defaultElement = AutomationManifestReader.RequireProperty(setting, "default", Scope);
			var defaultValue = ReadDefaultValue(key, type, defaultElement);

			var minimum = AutomationManifestReader.ReadOptionalNumber(setting, "minimum", Scope);
			var maximum = AutomationManifestReader.ReadOptionalNumber(setting, "maximum", Scope);
			var minimumLength = AutomationManifestReader.ReadOptionalInteger(setting, "minimumLength", Scope);
			var maximumLength = AutomationManifestReader.ReadOptionalInteger(setting, "maximumLength", Scope);
			var permittedValues = type is AutomationSettingType.Enum
				? ValidateEnumValues(setting, key, defaultValue.StringValue!)
				: ImmutableArray<string>.Empty;
			definitions.Add(new(
				key,
				displayName,
				description,
				type,
				defaultValue,
				minimum,
				maximum,
				minimumLength,
				maximumLength,
				permittedValues));
		}

		return definitions.ToImmutable();
	}

	/// <summary>
	/// The one place the manifest's spelling of a type is understood. Everything downstream carries
	/// <see cref="AutomationSettingType"/>, so no other code has to know that a type is written as text at all.
	/// </summary>
	private static AutomationSettingType ReadType(string key, string type)
		=> type switch
		{
			"boolean" => AutomationSettingType.Boolean,
			"integer" => AutomationSettingType.Integer,
			"number" => AutomationSettingType.Number,
			"string" => AutomationSettingType.String,
			"enum" => AutomationSettingType.Enum,
			"filePath" => AutomationSettingType.FilePath,
			"folderPath" => AutomationSettingType.FolderPath,
			_ => throw AutomationValidationException.Action($"Setting '{key}' has an invalid type or default value."),
		};

	private static AutomationSettingValue ReadDefaultValue(
		string key,
		AutomationSettingType type,
		JsonElement defaultElement)
	{
		var value = type switch
		{
			AutomationSettingType.Boolean when defaultElement.ValueKind is JsonValueKind.True or JsonValueKind.False =>
				new AutomationSettingValue(AutomationSettingValueKind.Boolean, BooleanValue: defaultElement.GetBoolean()),
			AutomationSettingType.Integer when defaultElement.TryGetInt64(out var integer) =>
				new AutomationSettingValue(AutomationSettingValueKind.Integer, IntegerValue: integer),
			AutomationSettingType.Number when defaultElement.TryGetDouble(out var number) && double.IsFinite(number) =>
				new AutomationSettingValue(AutomationSettingValueKind.Number, NumberValue: number),
			AutomationSettingType.String or AutomationSettingType.Enum or AutomationSettingType.FilePath or
				AutomationSettingType.FolderPath when defaultElement.ValueKind is JsonValueKind.String =>
				new AutomationSettingValue(AutomationSettingValueKind.String, StringValue: defaultElement.GetString()),
			_ => null,
		};
		return value ?? throw AutomationValidationException.Action($"Setting '{key}' has an invalid type or default value.");
	}

	private static ImmutableArray<string> ValidateEnumValues(JsonElement setting, string key, string defaultValue)
	{
		if (!setting.TryGetProperty("values", out var values) || values.ValueKind is not JsonValueKind.Array)
			throw AutomationValidationException.Action($"Enum setting '{key}' requires a values array.");
		var permittedValues = values.EnumerateArray()
			.Select(value => value.ValueKind is JsonValueKind.String
				? value.GetString()!
				: throw AutomationValidationException.Action($"Enum setting '{key}' values must be strings."))
			.ToImmutableArray();
		if (permittedValues.Length is 0 ||
			permittedValues.Distinct(StringComparer.Ordinal).Count() != permittedValues.Length ||
			!permittedValues.Contains(defaultValue, StringComparer.Ordinal))
		{
			throw AutomationValidationException.Action($"Enum setting '{key}' values are invalid.");
		}
		return permittedValues;
	}

	[GeneratedRegex(@"^[a-z][a-zA-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
	private static partial Regex SettingKeyRegex();
}
