// Copyright (c) VxFiles contributors
// Licensed under the MIT License.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VxFiles.Automation.Abstractions;

namespace Files.App.Dialogs
{
	/// <summary>
	/// Where an Automation Package is given the programs it needs and the settings its actions declare.
	/// </summary>
	/// <remarks>
	/// Package-scoped and applied in one call: the rail's pages are views, not save units, so a partial save cannot
	/// exist and Cancel discards everything without a write.
	/// </remarks>
	public sealed partial class AutomationConfigureDialog : ContentDialog
	{
		private Func<AutomationPackageConfiguration, Task>? _apply;

		private FrameworkElement RootAppElement
			=> (FrameworkElement)MainWindow.Instance.Content;

		public AutomationConfigureDialogViewModel ViewModel
		{
			get => (AutomationConfigureDialogViewModel)DataContext;
			set => DataContext = value;
		}

		public AutomationConfigureDialog()
		{
			InitializeComponent();
		}

		/// <summary>
		/// Shows the dialog for one package and applies what the user saved.
		/// </summary>
		/// <remarks>
		/// Applying happens while the dialog is still up, so a refusal can be shown against the boxes that caused
		/// it rather than after everything the user typed has gone. Save is disabled for everything the dialog can
		/// see coming, so a failure here means something moved underneath it.
		/// </remarks>
		public async Task ShowAsync(
			AutomationPackageSnapshot package,
			Func<AutomationPackageConfiguration, Task> apply)
		{
			ArgumentNullException.ThrowIfNull(package);
			ArgumentNullException.ThrowIfNull(apply);

			ViewModel = new(package);
			_apply = apply;
			await this.TryShowAsync();
		}

		private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			if (_apply is not { } apply)
				return;

			var deferral = args.GetDeferral();
			try
			{
				ViewModel.Failure = string.Empty;
				await apply(ViewModel.BuildConfiguration());
			}
			catch (Exception exception)
			{
				// Keep the dialog open on the values the user entered. Closing would discard a configuration that
				// is one corrected box away from being applicable.
				args.Cancel = true;
				ViewModel.Failure = exception.Message;
			}
			finally
			{
				deferral.Complete();
			}
		}

		/// <summary>
		/// Browses for a <c>filePath</c> or <c>folderPath</c> setting's value.
		/// </summary>
		/// <remarks>
		/// In the code-behind rather than on the row, because which picker opens is a host decision and the row is
		/// a projection of the manifest's declaration.
		/// </remarks>
		private async void SettingBrowse_Click(object sender, RoutedEventArgs e)
		{
			if (sender is not Button { Tag: AutomationConfigureSettingItem setting })
				return;

			if (setting.PicksFolder)
			{
				var folderPicker = new Windows.Storage.Pickers.FolderPicker
				{
					SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
				};
				folderPicker.FileTypeFilter.Add("*");
				WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, MainWindow.Instance.WindowHandle);
				if (await folderPicker.PickSingleFolderAsync() is { } folder)
					setting.TextValue = folder.Path;

				return;
			}

			var filePicker = new Windows.Storage.Pickers.FileOpenPicker
			{
				SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder,
			};
			filePicker.FileTypeFilter.Add("*");
			WinRT.Interop.InitializeWithWindow.Initialize(filePicker, MainWindow.Instance.WindowHandle);
			if (await filePicker.PickSingleFileAsync() is { } file)
				setting.TextValue = file.Path;
		}
	}
}
