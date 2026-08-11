// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify which control edits an Automation Action setting.
	/// </summary>
	/// <remarks>
	/// Fewer members than <c>AutomationSettingType</c>, because two declared types can share one control:
	/// <c>integer</c> and <c>number</c> are both a NumberBox, and a file path and a folder path are both a box with
	/// a Browse button beside it. What differs between those pairs is what the control is configured with, not
	/// which control it is.
	/// </remarks>
	public enum AutomationSettingEditor
	{
		/// <summary>
		/// A toggle, for <c>boolean</c>.
		/// </summary>
		Toggle,

		/// <summary>
		/// A NumberBox, for <c>integer</c> and <c>number</c>. Its declared bounds are the control's own, so a value
		/// outside them cannot be entered rather than being refused after the fact.
		/// </summary>
		Number,

		/// <summary>
		/// A text box, for <c>string</c>.
		/// </summary>
		Text,

		/// <summary>
		/// A dropdown over the declared values, for <c>enum</c>. The only editor whose contents come from the
		/// manifest rather than from the type.
		/// </summary>
		Choice,

		/// <summary>
		/// A text box with a Browse button, for <c>filePath</c> and <c>folderPath</c>. The picker it opens differs;
		/// the row does not.
		/// </summary>
		Path,
	}
}
