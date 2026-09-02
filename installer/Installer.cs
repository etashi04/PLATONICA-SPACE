using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Installer
{
    private const string Title = "PLATONICA SPACE 한국어 패치";

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        try
        {
            string gameDirectory = FindGameDirectory();
            if (gameDirectory == null) return 1;
            InstallPayload(gameDirectory);
            MessageBox.Show("한국어 패치 설치가 완료되었습니다.\n\n" + gameDirectory, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("설치하지 못했습니다.\n\n" + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static string FindGameDirectory()
    {
        var candidates = new List<string>();
        string steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!string.IsNullOrEmpty(steamPath))
        {
            candidates.Add(Path.Combine(steamPath, @"steamapps\common\PLATONICA SPACE"));
            string libraries = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
            if (File.Exists(libraries))
                foreach (string line in File.ReadAllLines(libraries))
                {
                    Match match = Regex.Match(line, "\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"");
                    if (match.Success) candidates.Add(Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"), @"steamapps\common\PLATONICA SPACE"));
                }
        }
        foreach (string candidate in candidates)
            if (File.Exists(Path.Combine(candidate, "platonica-space.exe"))) return candidate;

        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "platonica-space.exe가 있는 게임 설치 폴더를 선택하세요.";
            dialog.ShowNewFolderButton = false;
            if (dialog.ShowDialog() != DialogResult.OK) return null;
            if (!File.Exists(Path.Combine(dialog.SelectedPath, "platonica-space.exe")))
                throw new InvalidOperationException("선택한 폴더에서 platonica-space.exe를 찾지 못했습니다.");
            return dialog.SelectedPath;
        }
    }

    private static void InstallPayload(string gameDirectory)
    {
        string root = Path.GetFullPath(gameDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        {
            if (payload == null) throw new InvalidOperationException("내장 설치 데이터를 찾지 못했습니다.");
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destination = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("잘못된 설치 경로가 포함되어 있습니다.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
        }
        if (!File.Exists(Path.Combine(root, @"BepInEx\plugins\KR.LanguageFontPoc\KR.LanguageFontPoc.dll")))
            throw new IOException("설치 후 플러그인 DLL을 확인하지 못했습니다.");
    }
}
