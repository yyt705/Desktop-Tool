using System;
using System.IO;
using System.Linq;

namespace DeskFences
{
    internal static class RegressionTests
    {
        private static void ConfigTests(string dir)
        {
            string path = Path.Combine(dir, "settings.json");
            if (AppConfig.Load(path) != null) throw new Exception("Missing settings must request first-run initialization.");
            var config = new AppConfig();
            config.Save(path);
            var loaded = AppConfig.Load(path);
            if (loaded == null || loaded.Fences.Count != 0) throw new Exception("A saved empty layout must remain empty.");
            byte[] original = File.ReadAllBytes(path);
            File.SetAttributes(path, FileAttributes.ReadOnly);
            bool reported = false;
            try
            {
                config.Locked = true;
                config.Save(path);
            }
            catch (UnauthorizedAccessException) { reported = true; }
            catch (IOException) { reported = true; }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            if (!reported || !original.SequenceEqual(File.ReadAllBytes(path))) throw new Exception("An unsuccessful save must report failure and preserve the last layout.");
            config.Save(path);
            if (!AppConfig.Load(path).Locked) throw new Exception("Saving must recover after write access is restored.");
            string legacy = "{\"Fences\":[{\"MemberIconNames\":[\"Report\"]}]}";
            File.WriteAllText(path, legacy);
            var migrating = AppConfig.Load(path);
            migrating.Fences[0].MemberIconNames.Clear();
            migrating.Save(path);
            if (File.ReadAllText(Path.ChangeExtension(path, "legacy.json")) != legacy)
                throw new Exception("The first migration must keep the original layout.");

            // Case-insensitive MemberParsingNames and duplicate MemberIconIds cleanup across load
            string caseJson = "{\"Fences\":[{\"MemberIconIds\":[\"file:ABCD:01\",\"file:abcd:01\"],\"MemberParsingNames\":{\"FILE:ABCD:01\":\"C:\\\\Desktop\\\\a.txt\"}},{\"MemberIconIds\":[\"file:abcd:01\"]}]}";
            File.WriteAllText(path, caseJson);
            var reloaded = AppConfig.Load(path);
            string parsed;
            if (reloaded.Fences[0].MemberIconIds.Count != 1 || reloaded.Fences[1].MemberIconIds.Count != 0)
                throw new Exception("Duplicate MemberIconIds must be deduplicated on load.");
            if (!reloaded.Fences[0].MemberParsingNames.TryGetValue("file:abcd:01", out parsed) || parsed != @"C:\Desktop\a.txt")
                throw new Exception("MemberParsingNames must retain OrdinalIgnoreCase comparer after deserialization.");

            File.WriteAllText(path, "{");
            try { AppConfig.Load(path); }
            catch (InvalidDataException) { return; }
            throw new Exception("Damaged settings must not be treated as first run.");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string dir = Path.Combine(Path.GetTempPath(), "DeskFencesTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                MembershipTests.Run(dir); Console.WriteLine("PASS identity, migration, rename, replacement, reordered drag");
                ConfigTests(dir); Console.WriteLine("PASS configuration and save recovery");
                InputTests.Run(); Console.WriteLine("PASS input attribution");
                MonitorTests.Run(); Console.WriteLine("PASS monitor placement");
                DisplayProfileTests.Run(dir); Console.WriteLine("PASS multi-screen display profiles, member slots and loose icon adaptation");
                FenceCreationTests.Run(dir); Console.WriteLine("PASS icon-free new fence placement and minimal drag-wrap creation");
                LifecycleTests.Run(); Console.WriteLine("PASS drag and Explorer lifecycle");
                NativeMoveTests.Run(dir); Console.WriteLine("PASS native member positioning");
                DragFrameTests.Run(dir); Console.WriteLine("PASS native two-fence drag, selection, clipping and outsider collisions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Directory.Delete(dir, true); }
        }
    }
}
