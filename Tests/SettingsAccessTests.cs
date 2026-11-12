using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using JBZUniversalTester.Models;
using JBZUniversalTester.Services;
using JBZUniversalTester.ViewModels;
using JBZUniversalTester.Views;

namespace JBZUniversalTester.SelfTests;

internal static partial class Program
{
    private static void TestSettingsCredentialsAndPermissions()
    {
        string root = Path.Combine(Path.GetTempPath(), "JBZ-SettingsAccess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "access.json");
            var service = new SettingsAccessService(path);
            Assert(service.VerifyAdmin("admin") && !service.VerifyAdmin("Admin") && !service.VerifyAdmin(""),
                "Initial Admin password is case-sensitive and cannot be bypassed with an empty value");
            Assert(service.OperatorSession.Permissions == SettingsPermission.None,
                "New operator role starts read-only with no editable groups");
            bool Rejected(Action action)
            {
                try { action(); return false; }
                catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException) { return true; }
            }
            Assert(Rejected(() => service.ChangePassword(service.OperatorSession, "admin", "changed", "changed")) &&
                   Rejected(() => service.SetOperatorPermissions(service.OperatorSession, SettingsPermission.All)) &&
                   Rejected(() => service.ChangePassword(SettingsAccessSession.Administrator, "bad", "changed", "changed")) &&
                   Rejected(() => service.ChangePassword(SettingsAccessSession.Administrator, "admin", "changed", "different")),
                "Operator cannot administer access; wrong current password and mismatched confirmation never change credentials");
            service.ChangePassword(SettingsAccessSession.Administrator, "admin", "new-secret-42", "new-secret-42");
            service.SetOperatorPermissions(SettingsAccessSession.Administrator, SettingsPermission.Leak | SettingsPermission.Label);
            var reloaded = new SettingsAccessService(path);
            Assert(!reloaded.VerifyAdmin("admin") && reloaded.VerifyAdmin("new-secret-42") &&
                   reloaded.OperatorSession.Allows(SettingsPermission.Leak | SettingsPermission.Label) &&
                   !reloaded.OperatorSession.Allows(SettingsPermission.Io) && !reloaded.OperatorSession.IsAdmin,
                "Password changes and independent group permissions persist across application versions/reloads");
            string json = File.ReadAllText(path);
            Assert(!json.Contains("new-secret-42", StringComparison.Ordinal) && !json.Contains("\"admin\"", StringComparison.Ordinal) &&
                   File.Exists(path + ".backup") && Directory.GetFiles(root, "*.tmp").Length == 0,
                "Credential file contains salted hashes, atomic save leaves a backup and no temporary files");
            File.WriteAllText(path, "{broken");
            bool damagedRejected = false;
            try { _ = new SettingsAccessService(path); }
            catch (JsonException) { damagedRejected = true; }
            Assert(damagedRejected && File.ReadAllText(path) == "{broken",
                "Corrupt access data fails closed and cannot silently restore default Admin or overwrite credentials");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void TestSettingsPermissionSaveGuards()
    {
        int thresholdSaves = 0;
        var thresholdVm = new ProductionSettingsViewModel(saveSettings: _ => thresholdSaves++, initialSettings: new ProductionSettings());
        Assert(thresholdVm.ProbeReplacementThresholdText == "200.000", "PIN threshold defaults to 200.000 in settings");
        thresholdVm.SetAccessSession(new(false, SettingsPermission.Relay));
        foreach (string input in new[] { "150.000", "250000" })
        {
            thresholdVm.ProbeReplacementThresholdText = input;
            thresholdVm.Save();
        }
        Assert(thresholdSaves == 2 && thresholdVm.Settings.ProbeReplacementThreshold == 250000,
            "Relay maintenance permission can save grouped or plain PIN thresholds");
        foreach (string input in new[] { "0", "-1", "1.5", "abc", "", "9999999999999999999999" })
        {
            thresholdVm.ProbeReplacementThresholdText = input;
            bool invalidBlocked = false;
            try { thresholdVm.Save(); } catch (InvalidOperationException) { invalidBlocked = true; }
            Assert(invalidBlocked && thresholdSaves == 2, "Invalid PIN threshold cannot be silently saved");
        }

        foreach (SettingsPermission permission in new[] { SettingsPermission.Io, SettingsPermission.Relay,
            SettingsPermission.Leak, SettingsPermission.Resistance, SettingsPermission.Label })
        {
            int saves = 0;
            var vm = new ProductionSettingsViewModel(saveSettings: _ => saves++);
            vm.SetAccessSession(new(false, permission));
            switch (permission)
            {
                case SettingsPermission.Io: vm.Settings.DeviceName += "-allowed"; break;
                case SettingsPermission.Relay: vm.Settings.Relay1JigPulseMs++; break;
                case SettingsPermission.Leak: vm.WaterProof.PressMin++; break;
                case SettingsPermission.Resistance: vm.ResistanceChannels[0].MinOhm += 0.01; break;
                case SettingsPermission.Label: vm.Settings.Label.Copies++; break;
            }
            vm.Save();
            Assert(saves == 1, $"Operator can save its authorized {permission} group");
            if (permission == SettingsPermission.Io) vm.Settings.Label.Copies++;
            else vm.Settings.DeviceNumber += "-denied";
            bool denied = false;
            try { vm.SaveAsync().GetAwaiter().GetResult(); }
            catch (UnauthorizedAccessException) { denied = true; }
            Assert(denied && saves == 1, $"{permission} session cannot persist a change in another group through SaveAsync");
        }
        int readOnlySaves = 0;
        var readOnly = new ProductionSettingsViewModel(saveSettings: _ => readOnlySaves++);
        readOnly.SetAccessSession(new(false, SettingsPermission.None));
        bool blocked = false;
        try { readOnly.Save(); }
        catch (UnauthorizedAccessException) { blocked = true; }
        Assert(blocked && readOnlySaves == 0 && !readOnly.ManualRelay1OnCommand.CanExecute(null) &&
               !readOnly.ManualMeasureResistanceCommand.CanExecute(null) && !readOnly.ManualWaterProofTestCommand.CanExecute(null),
            "Read-only sessions cannot save settings or invoke manual hardware commands");
    }

    private static void TestSettingsPermissionUi()
    {
        OnSettingsDispatcher(async () =>
        {
            var vm = new ProductionSettingsViewModel(saveSettings: _ => { });
            var page = new ProductionSettingsPage(null, vm, deferInitialLayout: true);
            page.SetAccessSession(new(false, SettingsPermission.Leak));
            await page.PrepareForDisplayAsync();
            bool Enabled(string name) => ((UIElement)page.FindName(name)).IsEnabled;
            Assert(Enabled("WaterProofSettingsPanel") && !Enabled("IoMasterConfiguration") && !Enabled("IoGeneralConfiguration") &&
                   !Enabled("RelaySettingsPanel") && !Enabled("ManualRelayControlsGrid") && !Enabled("ResistanceSettingsPanel") &&
                   !Enabled("LabelSettingsPanel") && Enabled("SaveSettingsButton"),
                "Leak-only operator can use Leak while IO, Relay, resistance and printing remain disabled after staged opening");
            Assert(((Button)page.FindName("ChangeAdminPasswordButton")).Visibility == Visibility.Collapsed &&
                   ((Button)page.FindName("ManageSettingsPermissionsButton")).Visibility == Visibility.Collapsed,
                "Operators have no password-change or permission-administration actions");
            page.SetAccessSession(new(false, SettingsPermission.None));
            Assert(!Enabled("SaveSettingsButton"), "Read-only UI cannot re-enable Save after layout preparation");
            page.SetAccessSession(SettingsAccessSession.Administrator);
            Assert(Enabled("IoGeneralConfiguration") && Enabled("LabelSettingsPanel") && Enabled("ResistanceSettingsPanel") &&
                   ((Button)page.FindName("ChangeAdminPasswordButton")).Visibility == Visibility.Visible,
                "Admin receives all groups and the password-change action");
            page.ReleasePageResources();
        });
    }
}
