// Copyright (c) Files Community
// Licensed under the MIT License.

using System.Text.Json;
using VxFiles.Automation.Abstractions;

namespace VxFiles.Automation;

/// <summary>
/// Writes a setting value as JSON.
/// </summary>
/// <remarks>
/// Two places emit one: the state store persists it, and the runner transports it to the action. They have to
/// agree, because a value the store writes is a value the runner later hands over — a store that spelled a
/// number as text would reach the action as text.
/// </remarks>
internal static class AutomationSettingValueJson
{
	public static void Write(Utf8JsonWriter writer, AutomationSettingValue value)
	{
		switch (value.Kind)
		{
			case AutomationSettingValueKind.Boolean:
				writer.WriteBooleanValue(value.BooleanValue);
				break;
			case AutomationSettingValueKind.Integer:
				writer.WriteNumberValue(value.IntegerValue);
				break;
			case AutomationSettingValueKind.Number:
				writer.WriteNumberValue(value.NumberValue);
				break;
			case AutomationSettingValueKind.String:
				writer.WriteStringValue(value.StringValue);
				break;
			default:
				throw new InvalidOperationException("Unknown Automation setting value kind.");
		}
	}
}
