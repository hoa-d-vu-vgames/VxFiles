// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using VxFiles.Automation.Abstractions;

namespace Files.App.Data.Items
{
	/// <summary>
	/// One declared Automation Action setting as the Configure dialog edits it.
	/// </summary>
	/// <remarks>
	/// Opens on the schema's current value, which the snapshot already carries — that is what lets the dialog open
	/// with no read and makes Cancel free rather than a rollback.
	///
	/// <para>
	/// The declared bounds are handed to the control rather than checked afterwards: a NumberBox holds its own
	/// minimum and maximum, a dropdown offers only the declared values, and a text box carries its maximum length.
	/// A value the session would refuse is therefore mostly unreachable rather than reported, and this type holds
	/// no second copy of the rule the session screens by.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationConfigureSettingItem : ObservableObject
	{
		private readonly AutomationSettingSchema _schema;
		private readonly Action _changed;

		private bool _booleanValue;
		private double _numberValue;
		private string _textValue = string.Empty;

		public AutomationConfigureSettingItem(AutomationSettingSchema schema, Action changed)
		{
			ArgumentNullException.ThrowIfNull(schema);
			ArgumentNullException.ThrowIfNull(changed);

			_schema = schema;
			_changed = changed;
			Editor = schema.Type switch
			{
				AutomationSettingType.Boolean => AutomationSettingEditor.Toggle,
				AutomationSettingType.Integer or AutomationSettingType.Number => AutomationSettingEditor.Number,
				AutomationSettingType.Enum => AutomationSettingEditor.Choice,
				AutomationSettingType.FilePath or AutomationSettingType.FolderPath => AutomationSettingEditor.Path,
				_ => AutomationSettingEditor.Text,
			};

			_booleanValue = schema.CurrentValue.BooleanValue;
			_numberValue = schema.CurrentValue.Kind is AutomationSettingValueKind.Integer
				? schema.CurrentValue.IntegerValue
				: schema.CurrentValue.NumberValue;
			_textValue = schema.CurrentValue.StringValue ?? string.Empty;
		}

		public string Key => _schema.Key;

		public string DisplayName => _schema.DisplayName;

		public string Description => _schema.Description;

		public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

		public AutomationSettingEditor Editor { get; }

		public bool IsToggle => Editor is AutomationSettingEditor.Toggle;

		public bool IsNumber => Editor is AutomationSettingEditor.Number;

		public bool IsText => Editor is AutomationSettingEditor.Text;

		public bool IsChoice => Editor is AutomationSettingEditor.Choice;

		public bool IsPath => Editor is AutomationSettingEditor.Path;

		/// <summary>
		/// Gets whether Browse should open a folder picker rather than a file one.
		/// </summary>
		public bool PicksFolder => _schema.Type is AutomationSettingType.FolderPath;

		public ImmutableArray<string> Choices => _schema.Values;

		/// <summary>
		/// Gets the NumberBox's step. A declared <c>integer</c> moves by one and refuses a fraction; a
		/// <c>number</c> does neither.
		/// </summary>
		public double Step => _schema.Type is AutomationSettingType.Integer ? 1 : 0.1;

		public bool IsInteger => _schema.Type is AutomationSettingType.Integer;

		/// <summary>
		/// Gets the declared minimum, or negative infinity — which is what a NumberBox reads as "no minimum".
		/// </summary>
		public double Minimum => _schema.Minimum ?? double.NegativeInfinity;

		public double Maximum => _schema.Maximum ?? double.PositiveInfinity;

		/// <summary>
		/// Gets the declared maximum length, or zero — which is what a TextBox reads as "no limit".
		/// </summary>
		public int MaximumLength => _schema.MaximumLength ?? 0;

		public bool BooleanValue
		{
			get => _booleanValue;
			set
			{
				if (SetProperty(ref _booleanValue, value))
					_changed();
			}
		}

		public double NumberValue
		{
			get => _numberValue;
			set
			{
				// A NumberBox reports NaN while its text is not a number yet. Keeping the last good value means an
				// in-progress edit cannot submit one.
				if (double.IsNaN(value) || !SetProperty(ref _numberValue, value))
					return;

				_changed();
			}
		}

		public string TextValue
		{
			get => _textValue;
			set
			{
				if (SetProperty(ref _textValue, value))
					_changed();
			}
		}

		/// <summary>
		/// Gets this setting's value in the spelling its declared type calls for.
		/// </summary>
		/// <remarks>
		/// An <c>integer</c> is submitted integer-kinded and a <c>number</c> number-kinded even when the user typed
		/// a whole one, because the declaration decides what the value is rather than how it happens to look.
		/// </remarks>
		public AutomationSettingValue Value => _schema.Type switch
		{
			AutomationSettingType.Boolean => new(AutomationSettingValueKind.Boolean, BooleanValue: BooleanValue),
			AutomationSettingType.Integer => new(AutomationSettingValueKind.Integer, IntegerValue: (long)Math.Round(NumberValue)),
			AutomationSettingType.Number => new(AutomationSettingValueKind.Number, NumberValue: NumberValue),
			_ => new(AutomationSettingValueKind.String, StringValue: TextValue),
		};

		/// <summary>
		/// Gets whether the user moved this setting off what it opened on, so a Save mentions only what was edited.
		/// </summary>
		public bool IsChanged => Value != _schema.CurrentValue;
	}
}
