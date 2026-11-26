using System;
using System.Threading;
using System.Windows.Forms;

namespace DeskFences
{
    internal static class Program
    {
        private const string MutexName = "DeskFences_SingleInstance_Mutex_v1";

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("DeskFences 已经在运行中，请查看右下角系统托盘图标。", "DeskFences", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                NativeMethods.EnablePerMonitorDpiV2();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                AppConfig config;
                try { config = AppConfig.Load(); }
                catch (Exception ex)
                {
                    MessageBox.Show("无法载入分区布局。请检查配置文件后重新启动。\n" +
                        AppConfig.GetConfigPath() + "\n" + ex.Message, "DeskFences", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                using (var manager = new FenceManager(config)) Application.Run(manager);
            }
        }
    }
}
