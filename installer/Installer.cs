using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Installer
{
    const string Title="PLATONICA SPACE 한국어 패치", StateName=".platonica-space-korean-patch";
    const string SupportedHash="54FBC80F0FCC880C55B9659C46F9C53C262419C2B85FDD62AE53BC76DC218B2E";
    sealed class Item { public string Path,OldHash,NewHash; public bool Existed; }

    [STAThread] static int Main(){ Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm()); return 0; }

    sealed class MainForm:Form
    {
        readonly TextBox path=new TextBox();
        public MainForm(){
            Text=Title+" v1.0.0"; ClientSize=new Size(620,235); FormBorderStyle=FormBorderStyle.FixedDialog; MaximizeBox=false; StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",9);
            var h=new Label{Text="PLATONICA SPACE 비공식 한국어 패치",AutoSize=true,Font=new Font("Segoe UI",17,FontStyle.Bold),Location=new Point(28,22)};
            var s=new Label{Text="지원 게임: Steam 24960315 · 원본과 현재 패치 상태를 설치 전에 검증합니다.",AutoSize=true,Location=new Point(29,63)};
            var l=new Label{Text="게임 설치 폴더",AutoSize=true,Location=new Point(29,101)};
            path.Location=new Point(29,123); path.Size=new Size(474,25); path.Text=FindGame()??"";
            var b=new Button{Text="찾아보기",Location=new Point(513,121),Size=new Size(80,29)};
            var i=new Button{Text="한국어 패치 설치",Location=new Point(29,174),Size=new Size(178,36)};
            var r=new Button{Text="원본 복구",Location=new Point(219,174),Size=new Size(128,36)};
            b.Click+=(x,y)=>Browse(); i.Click+=(x,y)=>Run(true); r.Click+=(x,y)=>Run(false); Controls.AddRange(new Control[]{h,s,l,path,b,i,r});
        }
        void Browse(){ using(var d=new FolderBrowserDialog()){d.Description="platonica-space.exe가 있는 게임 설치 폴더를 선택하세요.";d.ShowNewFolderButton=false;if(d.ShowDialog()==DialogResult.OK)path.Text=d.SelectedPath;} }
        void Run(bool install){ try{string root=ValidateRoot(path.Text);if(install)Install(root);else Restore(root);MessageBox.Show(install?"한국어 패치 설치가 완료되었습니다.":"원본 복구가 완료되었습니다.",Title,MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception e){MessageBox.Show((install?"설치":"복구")+"하지 못했습니다. 작업 직전 상태로 자동 롤백했습니다.\n\n"+e.Message,Title,MessageBoxButtons.OK,MessageBoxIcon.Error);} }
    }

    static string ValidateRoot(string value){if(string.IsNullOrWhiteSpace(value))throw new InvalidOperationException("게임 설치 폴더를 선택하세요.");string root=Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar),exe=Path.Combine(root,"platonica-space.exe");if(!File.Exists(exe))throw new InvalidOperationException("platonica-space.exe를 찾지 못했습니다.");if(!Hash(exe).Equals(SupportedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("지원하지 않는 게임 파일입니다. 게임 업데이트 또는 외부 수정 여부를 확인하세요.");return root;}
    static string FindGame(){var c=new List<string>();string steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null)as string;if(!string.IsNullOrEmpty(steam)){c.Add(Path.Combine(steam,@"steamapps\common\PLATONICA SPACE"));string v=Path.Combine(steam,@"steamapps\libraryfolders.vdf");if(File.Exists(v))foreach(string line in File.ReadAllLines(v)){Match m=Regex.Match(line,"\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"");if(m.Success)c.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\","\\"),@"steamapps\common\PLATONICA SPACE"));}}foreach(string x in c)if(File.Exists(Path.Combine(x,"platonica-space.exe")))return x;return null;}

    static void Install(string root){
        string state=Path.Combine(root,StateName),manifest=Path.Combine(state,"manifest.tsv"),temp=Path.Combine(Path.GetTempPath(),"PLATONICA_KR_"+Guid.NewGuid().ToString("N"));var items=new List<Item>();
        if(File.Exists(manifest)){ValidateCurrent(root,state,Read(manifest));return;}
        try{
            Directory.CreateDirectory(temp);Stream payload=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");if(payload==null)throw new InvalidDataException("내장 설치 데이터를 찾지 못했습니다.");
            using(payload)using(var zip=new ZipArchive(payload,ZipArchiveMode.Read))foreach(var e in zip.Entries){if(string.IsNullOrEmpty(e.Name))continue;string rel=e.FullName.Replace('/',Path.DirectorySeparatorChar),dst=Safe(root,rel),src=Path.Combine(temp,"new",rel);Directory.CreateDirectory(Path.GetDirectoryName(src));using(var a=e.Open())using(var z=new FileStream(src,FileMode.Create,FileAccess.Write))a.CopyTo(z);items.Add(new Item{Path=rel,Existed=File.Exists(dst),OldHash=File.Exists(dst)?Hash(dst):"-",NewHash=Hash(src)});}
            if(Directory.Exists(state))Directory.Delete(state,true);Directory.CreateDirectory(Path.Combine(state,"backup"));
            foreach(var x in items)if(x.Existed){Copy(Path.Combine(root,x.Path),Path.Combine(state,"backup",x.Path));if(Hash(Path.Combine(state,"backup",x.Path))!=x.OldHash)throw new IOException("원본 백업 해시 검증 실패: "+x.Path);}
            Write(manifest,items);
            foreach(var x in items)Copy(Path.Combine(temp,"new",x.Path),Path.Combine(root,x.Path));
            foreach(var x in items)if(Hash(Path.Combine(root,x.Path))!=x.NewHash)throw new IOException("설치 파일 검증 실패: "+x.Path);
        }catch{RollbackInstall(root,state,items);throw;}finally{Delete(temp);}
    }
    static void Restore(string root){
        string state=Path.Combine(root,StateName),manifest=Path.Combine(state,"manifest.tsv");if(!File.Exists(manifest))throw new InvalidOperationException("이 설치기로 만든 원본 백업을 찾지 못했습니다.");var items=Read(manifest);ValidateCurrent(root,state,items);string temp=Path.Combine(Path.GetTempPath(),"PLATONICA_KR_RESTORE_"+Guid.NewGuid().ToString("N"));
        try{foreach(var x in items)Copy(Path.Combine(root,x.Path),Path.Combine(temp,x.Path));foreach(var x in items){string dst=Path.Combine(root,x.Path);if(x.Existed)Copy(Path.Combine(state,"backup",x.Path),dst);else if(File.Exists(dst))File.Delete(dst);}foreach(var x in items)if(x.Existed&&Hash(Path.Combine(root,x.Path))!=x.OldHash)throw new IOException("복구 파일 검증 실패: "+x.Path);Directory.Delete(state,true);}catch{foreach(var x in items){string src=Path.Combine(temp,x.Path);if(File.Exists(src))Copy(src,Path.Combine(root,x.Path));}throw;}finally{Delete(temp);}
    }
    static void ValidateCurrent(string root,string state,List<Item> items){foreach(var x in items){string now=Path.Combine(root,x.Path);if(!File.Exists(now)||Hash(now)!=x.NewHash)throw new InvalidOperationException("현재 패치 파일이 변경되었습니다: "+x.Path);if(x.Existed){string old=Path.Combine(state,"backup",x.Path);if(!File.Exists(old)||Hash(old)!=x.OldHash)throw new InvalidOperationException("원본 백업 파일이 손상되었습니다: "+x.Path);}}}
    static void RollbackInstall(string root,string state,List<Item> items){foreach(var x in items)try{string dst=Path.Combine(root,x.Path),old=Path.Combine(state,"backup",x.Path);if(x.Existed&&File.Exists(old))Copy(old,dst);else if(!x.Existed&&File.Exists(dst))File.Delete(dst);}catch{}Delete(state);}
    static string Safe(string root,string rel){string d=Path.GetFullPath(Path.Combine(root,rel)),p=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(!d.StartsWith(p,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("잘못된 설치 경로입니다.");return d;}
    static void Copy(string a,string b){Directory.CreateDirectory(Path.GetDirectoryName(b));File.Copy(a,b,true);}
    static string Hash(string p){using(var h=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
    static void Delete(string p){try{if(Directory.Exists(p))Directory.Delete(p,true);}catch{}}
    static void Write(string p,List<Item> a){var l=new List<string>();foreach(var x in a)l.Add(x.Path+"\t"+(x.Existed?"1":"0")+"\t"+x.OldHash+"\t"+x.NewHash);File.WriteAllLines(p,l.ToArray(),new UTF8Encoding(false));}
    static List<Item> Read(string p){var a=new List<Item>();foreach(string l in File.ReadAllLines(p,Encoding.UTF8)){string[] q=l.Split('\t');if(q.Length!=4)throw new InvalidDataException("백업 목록이 손상되었습니다.");a.Add(new Item{Path=q[0],Existed=q[1]=="1",OldHash=q[2],NewHash=q[3]});}return a;}
}
