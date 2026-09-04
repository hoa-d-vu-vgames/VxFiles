// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

namespace Files.App.Data.Enums
{
	/// <summary>
	/// Defines constants that specify which control edits an Automation Action setting.
	/// </summary>
	public enum AutomationSettingEditor
	{
		/// <summary>
		/// A toggle, for <c>boolean</c>.
		/// </summary>
		Toggle,

		/// <summary>
		/// A text box for an exact signed 64-bit <c>integer</c>, avoiding a NumberBox's double conversion.
		/// </summary>
		Integer,

		/// <summary>
		/// A NumberBox for <c>number</c>. Its declared bounds are the control's own.
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
