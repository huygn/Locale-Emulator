﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using LECommonLibrary;

namespace LEProc
{
    internal static class Program
    {
        internal static string[] Args;

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            SystemHelper.DisableDPIScale();

            try
            {
                Process.Start(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                                           "LEUpdater.exe"),
                              "schedule");
            }
            catch
            {
            }

            if (!GlobalHelper.CheckCoreDLLs())
            {
                MessageBox.Show(
                                "Some of the core Dlls are missing.\r\n" +
                                "Please whitelist these Dlls in your antivirus software, then download and re-install LE.\r\n"
                                +
                                "\r\n" +
                                "These Dlls are:\r\n" +
                                "LoaderDll.dll\r\n" +
                                "LocaleEmulator.dll",
                                "Locale Emulator Version " + GlobalHelper.GetLEVersion(),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);

                return;
            }

            if (!File.Exists(LEConfig.GlobalConfigPath))
            {
                MessageBox.Show(
                                "\"LEConfig.xml\" not found. \r\n" +
                                "Please run \"LEGUI.exe\" once to let it generate one for you.",
                                "Locale Emulator Version " + GlobalHelper.GetLEVersion(),
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
            }

            if (args.Length == 0)
            {
                MessageBox.Show(
                                "Welcome to Locale Emulator command line tool.\r\n" +
                                "\r\n" +
                                "Usage: LEProc.exe\r\n" +
                                "\t<APP_PATH>\t\t\t--(1)\r\n" +
                                "\t-run <APP_PATH>\t\t\t--(2)\r\n" +
                                "\t-manage <APP_PATH>\t\t--(3)\r\n" +
                                "\t-global\t\t\t\t--(4)\r\n" +
                                "\t-runas <PROFILE_GUID> <APP_PATH>\t--(5)\r\n" +
                                "\r\n" +
                                "(1): Run an application with a build-in profile(as Japanese).\r\n" +
                                "(2): Run an application with it's own profile.\r\n" +
                                "(3): Modify the profile of one application.\r\n" +
                                "(4): Open Global Profile Manager.\r\n" +
                                "(5): Run an application with a global profile of specific Guid.\r\n" +
                                "\r\n" +
                                "\r\n" +
                                "Have a suggestion? Want to report a bug? You are welcome! \r\n" +
                                "Go to https://github.com/xupefei/Locale-Emulator/issues,\r\n" +
                                "or send a message to https://google.com/+PaddyXu.\r\n" +
                                "\r\n" +
                                "\r\n" +
                                "You can press CTRL+C to copy this message to your clipboard.\r\n",
                                "Locale Emulator Version " + GlobalHelper.GetLEVersion()
                    );

                GlobalHelper.ShowErrorDebugMessageBox("SYSTEM_REPORT", 0);

                return;
            }

            try
            {
                Args = args;

                switch (args[0])
                {
                    case "-run": //-run %APP%
                        RunWithIndependentProfile(args[1]);
                        break;

                    case "-manage": //-manage %APP%
                        Process.Start(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                                                   "LEGUI.exe"),
                                      $"\"{args[1]}.le.config\"");
                        break;

                    case "-global": //-global
                        Process.Start(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                                                   "LEGUI.exe"));
                        break;

                    case "-runas": //-runas %GUID% %APP%
                        RunWithGlobalProfile(args[1], args[2]);
                        break;

                    default: // Run with default profile
                        if (File.Exists(args[0]))
                            RunWithDefaultProfile(args[0]);
                        break;
                }
            }
            catch
            {
            }
        }

        private static void RunWithDefaultProfile(string path)
        {
            DoRunWithLEProfile(path, 1, new LEProfile(true));
        }

        private static void RunWithGlobalProfile(string guid, string path)
        {
            // We do not check whether the config exists because only when it exists can this method be called.

            var profile = LEConfig.GetProfiles().First(p => p.Guid == guid);

            DoRunWithLEProfile(path, 3, profile);
        }

        private static void RunWithIndependentProfile(string path)
        {
            var conf = path + ".le.config";

            if (!File.Exists(conf))
            {
                Process.Start(
                              Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "LEGUI.exe"),
                              $"\"{path}.le.config\"");
            }
            else
            {
                var profile = LEConfig.GetProfiles(conf)[0];
                DoRunWithLEProfile(path, 2, profile);
            }
        }

        private static void DoRunWithLEProfile(string path, int argumentsStart, LEProfile profile)
        {
            try
            {
                if (profile.RunAsAdmin && !SystemHelper.IsAdministrator())
                {
                    ElevateProcess();

                    return;
                }

                if (profile.RunWithSuspend)
                {
                    if (DialogResult.No ==
                        MessageBox.Show(
                                        "You are running a exectuable with CREATE_SUSPENDED flag.\r\n" +
                                        "\r\n" +
                                        "The exectuable will be executed after you click the \"Yes\" button, " +
                                        "But as a background process which has no notfications at all." +
                                        "You can attach it by using OllyDbg, or stop it with Task Manager.\r\n",
                                        "Locale Emulator Debug Mode Warning",
                                        MessageBoxButtons.YesNo,
                                        MessageBoxIcon.Information
                            ))
                        return;
                }

                var applicationName = string.Empty;
                var commandLine = string.Empty;

                if (Path.GetExtension(path).ToLower() == ".exe")
                {
                    applicationName = path;

                    commandLine = path.StartsWith("\"")
                                      ? $"{path} "
                                      : $"\"{path}\" ";

                    // use arguments in le.config, prior to command line arguments
                    commandLine += string.IsNullOrEmpty(profile.Parameter) && Args.Skip(argumentsStart).Any()
                                       ? Args.Skip(argumentsStart).Aggregate((a, b) => $"{a} {b}")
                                       : profile.Parameter;
                }
                else
                {
                    var jb = AssociationReader.GetAssociatedProgram(Path.GetExtension(path));

                    if (jb == null)
                        return;

                    applicationName = jb[0];

                    commandLine = jb[0].StartsWith("\"")
                                      ? $"{jb[0]} "
                                      : $"\"{jb[0]}\" ";
                    commandLine += jb[1].Replace("%1", path).Replace("%*", profile.Parameter);
                }

                var currentDirectory = Path.GetDirectoryName(path);
                var debugMode = profile.RunWithSuspend;
                var ansiCodePage = (uint)CultureInfo.GetCultureInfo(profile.Location).TextInfo.ANSICodePage;
                var oemCodePage = (uint)CultureInfo.GetCultureInfo(profile.Location).TextInfo.OEMCodePage;
                var localeID = (uint)CultureInfo.GetCultureInfo(profile.Location).TextInfo.LCID;
                var defaultCharset = (uint)
                                     GetCharsetFromANSICodepage(CultureInfo.GetCultureInfo(profile.Location)
                                                                           .TextInfo.ANSICodePage);

                var registries = profile.RedirectRegistry
                                     ? new LERegistry().GetRegistryEntries()
                                     : new LERegistryEntry[0];

                var l = new LoaderWrapper
                        {
                            ApplicationName = applicationName,
                            CommandLine = commandLine,
                            CurrentDirectory = currentDirectory,
                            AnsiCodePage = ansiCodePage,
                            OemCodePage = oemCodePage,
                            LocaleID = localeID,
                            DefaultCharset = defaultCharset,
                            Timezone = profile.Timezone,
                            NumberOfRegistryRedirectionEntries = registries.Length,
                            DebugMode = debugMode
                        };

                registries.ToList()
                          .ForEach(
                                   item =>
                                   l.AddRegistryRedirectEntry(item.Root, item.Key, item.Name, item.Type, item.Data));

                uint ret;
                if ((ret = l.Start()) != 0)
                {
                    if (SystemHelper.IsAdministrator())
                    {
                        GlobalHelper.ShowErrorDebugMessageBox(commandLine, ret);
                    }
                    else
                    {
                        ElevateProcess();
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString());
            }
        }

        private static void ElevateProcess()
        {
            try
            {
                SystemHelper.RunWithElevatedProcess(
                                                    Path.Combine(
                                                                 Path.GetDirectoryName(
                                                                                       Assembly.GetExecutingAssembly()
                                                                                               .Location),
                                                                 "LEProc.exe"),
                                                    Args);
            }
            catch (Exception)
            {
            }
        }

        private static int GetCharsetFromANSICodepage(int ansicp)
        {
            const int ANSI_CHARSET = 0;
            const int DEFAULT_CHARSET = 1;
            const int SYMBOL_CHARSET = 2;
            const int SHIFTJIS_CHARSET = 128;
            const int HANGEUL_CHARSET = 129;
            const int HANGUL_CHARSET = 129;
            const int GB2312_CHARSET = 134;
            const int CHINESEBIG5_CHARSET = 136;
            const int OEM_CHARSET = 255;
            const int JOHAB_CHARSET = 130;
            const int HEBREW_CHARSET = 177;
            const int ARABIC_CHARSET = 178;
            const int GREEK_CHARSET = 161;
            const int TURKISH_CHARSET = 162;
            const int VIETNAMESE_CHARSET = 163;
            const int THAI_CHARSET = 222;
            const int EASTEUROPE_CHARSET = 238;
            const int RUSSIAN_CHARSET = 204;
            const int MAC_CHARSET = 77;
            const int BALTIC_CHARSET = 186;

            var charset = ANSI_CHARSET;

            switch (ansicp)
            {
                case 932: // Japanese
                    charset = SHIFTJIS_CHARSET;
                    break;
                case 936: // Simplified Chinese
                    charset = GB2312_CHARSET;
                    break;
                case 949: // Korean
                    charset = HANGEUL_CHARSET;
                    break;
                case 950: // Traditional Chinese
                    charset = CHINESEBIG5_CHARSET;
                    break;
                case 1250: // Eastern Europe
                    charset = EASTEUROPE_CHARSET;
                    break;
                case 1251: // Russian
                    charset = RUSSIAN_CHARSET;
                    break;
                case 1252: // Western European Languages
                    charset = ANSI_CHARSET;
                    break;
                case 1253: // Greek
                    charset = GREEK_CHARSET;
                    break;
                case 1254: // Turkish
                    charset = TURKISH_CHARSET;
                    break;
                case 1255: // Hebrew
                    charset = HEBREW_CHARSET;
                    break;
                case 1256: // Arabic
                    charset = ARABIC_CHARSET;
                    break;
                case 1257: // Baltic
                    charset = BALTIC_CHARSET;
                    break;
            }

            return charset;
        }
    }
}