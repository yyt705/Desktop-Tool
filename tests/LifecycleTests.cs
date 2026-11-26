using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace DeskFences
{
    internal static class LifecycleTests
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static bool SnapEnabled(ListView view)
        {
            int styles = (int)NativeMethods.SendMessage(view.Handle,
                NativeMethods.LVM_GETEXTENDEDLISTVIEWSTYLE, IntPtr.Zero, IntPtr.Zero);
            return (styles & NativeMethods.LVS_EX_SNAPTOGRID) != 0;
        }

        // Real, hidden HWNDs in the test process exercise Win32 parenting and list-view styles.
        private static void TestViewReplacement()
        {
            using (var owner = new Form())
            using (var folder = new Panel())
            using (var first = new ListView())
            using (var next = new ListView())
            using (var controller = new DesktopIconController())
            using (var backdrop = new FenceBackdropForm())
            {
                owner.Controls.Add(folder);
                folder.Controls.Add(first);
                folder.Controls.Add(next);
                Check(controller.BindHandles(owner.Handle, folder.Handle, first.Handle), "Initial test view must bind");
                int generation = controller.HostGeneration;
                controller.BindHandles(owner.Handle, folder.Handle, first.Handle);
                Check(controller.HostGeneration == generation, "Refreshing the same view must preserve an active session");
                IntPtr snap = new IntPtr(NativeMethods.LVS_EX_SNAPTOGRID);
                NativeMethods.SendMessage(first.Handle, NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE, snap, snap);
                NativeMethods.SendMessage(next.Handle, NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE, snap, IntPtr.Zero);
                controller.SuspendSnapToGrid();
                controller.SuspendSnapToGrid();
                Check(!SnapEnabled(first), "Snap must stay suspended through repeat acquisitions");
                Check(controller.BindHandles(owner.Handle, folder.Handle, next.Handle), "Replacement must bind");
                Check(controller.HostGeneration != generation && SnapEnabled(first), "Replacing a live view must release its snap setting");
                controller.RestoreSnapToGrid();
                Check(!SnapEnabled(next), "A replacement view must keep its own snap setting");
                NativeMethods.SendMessage(next.Handle, NativeMethods.LVM_SETEXTENDEDLISTVIEWSTYLE, snap, snap);
                controller.SuspendSnapToGrid();
                controller.RestoreSnapToGrid();
                Check(SnapEnabled(next), "Completing a gesture restores the active view's snap setting");
                next.Dispose();
                Check(!controller.HasCurrentHandles(), "A destroyed view must be rejected immediately");
                Check(!controller.BindHandles(owner.Handle, folder.Handle, IntPtr.Zero), "An incomplete hierarchy must remain unavailable");

                backdrop.AttachToInterlayer(folder.Handle, NativeMethods.HWND_TOP, owner.Handle);
                Check(NativeMethods.GetParent(backdrop.Handle) == folder.Handle, "Backdrop must attach to its interlayer");
                backdrop.AttachToInterlayer(IntPtr.Zero, IntPtr.Zero, owner.Handle);
                uint style = unchecked((uint)NativeMethods.GetWindowLong(backdrop.Handle, NativeMethods.GWL_STYLE));
                Check((style & NativeMethods.WS_CHILD) == 0 && NativeMethods.GetParent(backdrop.Handle) == owner.Handle,
                    "Losing the interlayer must convert the backdrop to an owned popup");
                backdrop.AttachToInterlayer(folder.Handle, NativeMethods.HWND_TOP, owner.Handle);
                Check(NativeMethods.GetParent(backdrop.Handle) == folder.Handle, "A later interlayer must be attachable");
            }
        }

        private static FieldInfo Field(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static void TestInterruptedGestures()
        {
            // The production manager constructor installs desktop hooks. Build only the inert fields
            // used by the form's interruption path so this regression never opens or moves desktop icons.
            var manager = (FenceManager)FormatterServices.GetUninitializedObject(typeof(FenceManager));
            var config = new AppConfig();
            var fence = new FenceConfig();
            config.Fences.Add(fence);
            Field(manager, "<Config>k__BackingField").SetValue(manager, config);
            Field(manager, "_exiting").SetValue(manager, true);
            using (var controller = new DesktopIconController())
            using (var form = new FenceChromeForm(manager, fence))
            {
                Field(manager, "_controller").SetValue(manager, controller);
                var metrics = new DesktopGridMetrics { CellWidth = 75, CellHeight = 90 };
                Field(form, "_dragMetrics").SetValue(form, metrics);
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    Field(manager, "_isMutatingFence").SetValue(manager, true);
                    Field(form, "_isDragging").SetValue(form, true);
                    Field(form, "_capturedMembers").SetValue(form, new List<DesktopIconInfo>());
                    if (attempt == 0)
                    {
                        typeof(FenceChromeForm).GetMethod("OnMouseCaptureChanged", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, new object[] { EventArgs.Empty });
                    }
                    else
                    {
                        typeof(FenceChromeForm).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(form, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
                    }
                    Check(!(bool)Field(manager, "_isMutatingFence").GetValue(manager), "Interrupted gestures must release the manager");
                    Check(!(bool)Field(form, "_releasePending").GetValue(form) &&
                        Field(form, "_capturedMembers").GetValue(form) == null &&
                        !((Timer)Field(form, "_animTimer").GetValue(form)).Enabled, "A previous gesture must not resume on the next input");
                    form.SettleInteraction(false, false);
                }

                Field(manager, "_isMutatingFence").SetValue(manager, true);
                FieldInfo edge = Field(form, "_resizeEdge");
                edge.SetValue(form, Enum.Parse(edge.FieldType, "Right"));
                Field(form, "_resizeMetrics").SetValue(form, metrics);
                form.RecoverMissedMouseUp();
                Check(!(bool)Field(manager, "_isMutatingFence").GetValue(manager) &&
                    edge.GetValue(form).ToString() == "None", "Watchdog must finish a resize with a missed button release");
            }
        }

        public static void Run()
        {
            TestViewReplacement();
            TestInterruptedGestures();
        }
    }
}
