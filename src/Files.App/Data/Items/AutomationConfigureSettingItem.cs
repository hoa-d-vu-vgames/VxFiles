// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Globalization;
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
	/// The controls enforce the constraints they support directly: a NumberBox holds its minimum and maximum, a
	/// dropdown offers only declared values, and a text box carries its maximum length. The shared value rules
	/// cover the remaining constraints before Save is enabled.
	/// </para>
	/// </remarks>
	public sealed partial class AutomationConfigureSettingItem : ObservableObject
	{
		private readonly AutomationSettingSchema _schema;
		private readonly Action _onChanged;

		private bool _booleanValue;
		private string _integerText = string.Empty;
		private double _numberValue;
		private string _textValue = string.Empty;

		public AutomationConfigureSettingItem(AutomationSettingSchema schema, Action onChanged)
		{
			ArgumentNullException.ThrowIfNull(schema);
			ArgumentNullException.ThrowIfNull(onChanged);

			_schema = schema;
			_onChanged = onChanged;
			Editor = schema.Type switch
			{
				AutomationSettingType.Boolean => AutomationSettingEditor.Toggle,
				AutomationSettingType.Integer => AutomationSettingEditor.Integer,
				AutomationSettingType.Number => AutomationSettingEditor.Number,
				AutomationSettingType.Enum => AutomationSettingEditor.Choice,
				AutomationSettingType.FilePath or AutomationSettingType.FolderPath => AutomationSettingEditor.Path,
				_ => AutomationSettingEditor.Text,
			};

			_booleanValue = schema.CurrentValue.BooleanValue;
			_integerText = schema.CurrentValue.IntegerValue.ToString(CultureInfo.InvariantCulture);
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

		public bool IsInteger => Editor is AutomationSettingEditor.Integer;

		public bool IsNumber => Editor is AutomationSettingEditor.Number;

		public bool IsText => Editor is AutomationSettingEditor.Text;

		public bool IsChoice => Editor is AutomationSettingEditor.Choice;

		public bool IsPath => Editor is AutomationSettingEditor.Path;

		public bool IsAcceptable => TryGetValue(out _);

		/// <summary>
		/// Gets whether Browse should open a folder picker rather than a file one.
		/// </summary>
		public bool PicksFolder => _schema.Type is AutomationSettingType.FolderPath;

		public ImmutableArray<string> Choices => _schema.Values;

		/// <summary>
		/// Gets the NumberBox's step for a <c>number</c> setting.
		/// </summary>
		public double Step => 0.1;

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
					_onChanged();
			}
		}

		public string IntegerText
		{
			get => _integerText;
			set
			{
				if (!SetProperty(ref _integerText, value))
					return;

				OnPropertyChanged(nameof(IsAcceptable));
				_onChanged();
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

				OnPropertyChanged(nameof(IsAcceptable));
				_onChanged();
			}
		}

		public string TextValue
		{
			get => _textValue;
			set
			{
				if (SetProperty(ref _textValue, value))
					_onChanged();
			}
		}

		/// <summary>
		/// Gets this setting's value in the spelling its declared type calls for.
		/// </summary>
		/// <remarks>
		/// An <c>integer</c> is submitted integer-kinded and a <c>number</c> number-kinded even when the user typed
		/// a whole one, because the declaration decides what the value is rather than how it happens to look.
		/// </remarks>
		public AutomationSettingValue Value
			=> TryGetValue(out var value)
				? value
				: throw new InvalidOperationException($"Setting '{Key}' does not contain an acceptable value.");

		/// <summary>
		/// Gets whether the user moved this setting off what it opened on, so a Save mentions only what was edited.
		/// </summary>
		public bool IsChanged => !TryGetValue(out var value) || value != _schema.CurrentValue;

		private bool TryGetValue(out AutomationSettingValue value)
		{
			AutomationSettingValue candidate;
			if (_schema.Type is AutomationSettingType.Integer)
			{
				if (!long.TryParse(IntegerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
				{
					value = null!;
					return false;
				}

				candidate = new(AutomationSettingValueKind.Integer, IntegerValue: integer);
			}
			else
			{
				candidate = _schema.Type switch
				{
					AutomationSettingType.Boolean => new(AutomationSettingValueKind.Boolean, BooleanValue: BooleanValue),
					AutomationSettingType.Number => new(AutomationSettingValueKind.Number, NumberValue: NumberValue),
					_ => new(AutomationSettingValueKind.String, StringValue: TextValue),
				};
			}

			return AutomationSettingValueRules.TryResolve(_schema, candidate, out value);
		}
	}
}
