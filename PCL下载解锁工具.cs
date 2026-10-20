using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Windows.Forms;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MethodBody = Mono.Cecil.Cil.MethodBody;

namespace PclUnlocker
{
    sealed class Analysis
    {
        public readonly bool CanPatch;
        public readonly string Message;

        public Analysis(bool canPatch, string message)
        {
            CanPatch = canPatch;
            Message = message;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveCecil;

            if (args.Length >= 2 && args[0] == "--analyze")
            {
                try
                {
                    Analysis a = Core.Analyze(args[1]);
                    string head = a.CanPatch ? "PATCHABLE" : "NO";
                    if (args.Length >= 3) File.WriteAllText(args[2], head + "\r\n" + a.Message);
                }
                catch (Exception ex)
                {
                    if (args.Length >= 3) File.WriteAllText(args[2], "ERROR\r\n" + ex.ToString());
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--patch")
            {
                try
                {
                    Core.Patch(args[1], args[2]);
                    File.WriteAllText(args[2] + ".log.txt", "OK");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[2] + ".log.txt", ex.ToString());
                }
                return;
            }
            if (args.Length >= 2 && args[0] == "--unlock")
            {
                try
                {
                    Core.PatchInPlace(args[1]);
                    File.WriteAllText(args[1] + ".unlock.log.txt", "OK");
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".unlock.log.txt", ex.ToString());
                }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static Assembly ResolveCecil(object sender, ResolveEventArgs e)
        {
            if (!e.Name.StartsWith("Mono.Cecil", StringComparison.Ordinal)) return null;
            Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll");
            if (s == null) return null;
            using (s)
            {
                byte[] buf = new byte[s.Length];
                s.Read(buf, 0, buf.Length);
                return Assembly.Load(buf);
            }
        }
    }

    sealed class MainForm : Form
    {
        readonly Label dropZone;
        readonly Label status;
        readonly Button btnPatch;
        readonly Button btnOpen;

        string currentPath;

        public MainForm()
        {
            Text = "PCL 下载解锁工具";
            ClientSize = new Size(580, 350);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.White;

            dropZone = new Label();
            dropZone.SetBounds(20, 18, 540, 170);
            dropZone.BorderStyle = BorderStyle.FixedSingle;
            dropZone.TextAlign = ContentAlignment.MiddleCenter;
            dropZone.BackColor = Color.FromArgb(245, 247, 250);
            dropZone.ForeColor = Color.FromArgb(70, 80, 90);
            dropZone.AllowDrop = true;
            dropZone.Text = "把 Plain Craft Launcher 2.exe 拖到这里\r\n\r\n（也可以点击此处选择文件）";
            dropZone.DragEnter += DropZoneDragEnter;
            dropZone.DragDrop += DropZoneDragDrop;
            dropZone.Click += delegate { BrowseFile(); };
            Controls.Add(dropZone);

            status = new Label();
            status.SetBounds(20, 200, 540, 46);
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Color.FromArgb(90, 90, 90);
            status.Text = "等待拖入文件……";
            Controls.Add(status);

            btnPatch = new Button();
            btnPatch.SetBounds(330, 258, 110, 34);
            btnPatch.Text = "开始解锁";
            btnPatch.Enabled = false;
            btnPatch.Click += delegate { StartPatch(); };
            Controls.Add(btnPatch);

            btnOpen = new Button();
            btnOpen.SetBounds(450, 258, 110, 34);
            btnOpen.Text = "打开文件夹";
            btnOpen.Visible = false;
            btnOpen.Click += delegate { OpenFolder(); };
            Controls.Add(btnOpen);

            Label tip = new Label();
            tip.SetBounds(20, 300, 540, 44);
            tip.ForeColor = Color.FromArgb(140, 140, 140);
            tip.Font = new Font("Microsoft YaHei UI", 8f);
            tip.Text = "修改时请关闭PCL；该程序由bilibili：TideOrca潮汐小虎鲸 制作；该文件绝对免费，如果你是通过收费渠道获得，请选择退款。";
            Controls.Add(tip);
        }

        void DropZoneDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.None;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return;
            if (files[0].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                e.Effect = DragDropEffects.Copy;
        }

        void DropZoneDragDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length != 1) return;
            SetFile(files[0]);
        }

        void BrowseFile()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 PCL 主程序";
                dlg.Filter = "PCL 主程序|Plain Craft Launcher*.exe|可执行文件|*.exe";
                if (dlg.ShowDialog(this) == DialogResult.OK) SetFile(dlg.FileName);
            }
        }

        void SetFile(string path)
        {
            currentPath = path;
            btnOpen.Visible = false;
            btnPatch.Enabled = false;

            try
            {
                Analysis a = Core.Analyze(path);
                status.Text = a.Message;
                status.ForeColor = a.CanPatch
                    ? Color.FromArgb(0, 120, 60)
                    : Color.FromArgb(150, 80, 40);
                dropZone.Text = Path.GetFileName(path) + "\r\n\r\n" + a.Message;
                btnPatch.Enabled = a.CanPatch;
            }
            catch (Exception ex)
            {
                status.Text = "读取失败：" + ex.Message;
                status.ForeColor = Color.FromArgb(180, 40, 40);
                dropZone.Text = Path.GetFileName(path) + "\r\n\r\n无法读取或不是有效的 .NET 程序";
            }
        }

        void StartPatch()
        {
            if (currentPath == null) return;
            string input = currentPath;

            DialogResult confirm = MessageBox.Show(this,
                "将直接修改并覆盖：\r\n" + input + "\r\n\r\n文件名和位置保持不变，不会保留原文件备份。\r\n确定继续吗？",
                "确认解锁", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes) return;

            btnPatch.Enabled = false;
            UseWaitCursor = true;
            status.Text = "正在就地解锁，请稍候……";
            status.ForeColor = Color.FromArgb(90, 90, 90);

            BackgroundWorker worker = new BackgroundWorker();
            worker.DoWork += delegate(object s, DoWorkEventArgs e)
            {
                try
                {
                    Core.PatchInPlace(input);
                    e.Result = null;
                }
                catch (Exception ex)
                {
                    e.Result = ex;
                }
            };
            worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs e)
            {
                UseWaitCursor = false;
                Exception error = e.Result as Exception;
                if (error != null)
                {
                    status.Text = "解锁失败：" + error.Message;
                    status.ForeColor = Color.FromArgb(180, 40, 40);
                    btnPatch.Enabled = true;
                    MessageBox.Show(this, error.Message, "解锁失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                status.Text = "解锁完成，文件名未改变。";
                status.ForeColor = Color.FromArgb(0, 120, 60);
                dropZone.Text = "已解锁\r\n\r\n" + Path.GetFileName(input);
                btnOpen.Visible = true;
                MessageBox.Show(this,
                    "已完成就地解锁：\r\n" + input + "\r\n\r\n文件名与位置保持不变。",
                    "解锁完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            worker.RunWorkerAsync();
        }

        void OpenFolder()
        {
            if (currentPath == null || !File.Exists(currentPath)) return;
            try
            {
                Process.Start("explorer.exe", "/select,\"" + currentPath + "\"");
            }
            catch { }
        }
    }

    static class Core
    {
        // ---------------- analyze ----------------

        public static Analysis Analyze(string path)
        {
            AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Immediate });
            try
            {
                ModuleDefinition mod = asm.MainModule;

                if (asm.Name.Name != "Plain Craft Launcher 2")
                    return new Analysis(false, "这不是 PCL 主程序（程序集名称：" + asm.Name.Name + "）。");

                string version = asm.Name.Version.ToString();

                TypeDefinition secret = FindTypeOrNull(mod, "PCL.ModSecret");
                FieldDefinition restrict = null;
                if (secret != null)
                    restrict = secret.Fields.FirstOrDefault(f => f.Name == "RestrictMcDownload");

                TypeDefinition mm = FindTypeOrNull(mod, "PCL.ModModpack");
                MethodDefinition m3 = mm == null ? null : FindMethodOrNull(mm, "ModpackInstall", 3);
                TypeDefinition page = FindTypeOrNull(mod, "PCL.PageDownloadInstall");
                MethodDefinition loader = page == null ? null : FindMethodOrNull(page, "LoaderInit", 0);

                bool dispatchPatched = m3 != null && m3.HasBody &&
                                       FindRestrictionThrow(m3) == null && HasDispatch(m3);
                bool loaderPatched = loader != null && loader.HasBody &&
                                     FindDlLoaderInMethod(loader) != null;
                bool constantOn = restrict != null && restrict.Constant is bool && (bool)restrict.Constant;

                if (!constantOn)
                {
                    if (dispatchPatched && loaderPatched)
                        return new Analysis(false, "该文件已经是解锁状态，无需重复处理。");
                    return new Analysis(false, "该版本没有 MC 下载限制，无需解锁。");
                }

                if (dispatchPatched && loaderPatched)
                    return new Analysis(false, "该文件已经是解锁状态，无需重复处理。");
                if (m3 == null || !m3.HasBody)
                    return new Analysis(false, "文件结构不匹配：未找到整合包安装方法。");
                if (FindRestrictionThrow(m3) == null)
                    return new Analysis(false, "文件结构不匹配：未找到下载限制逻辑（可能已处理或版本不同）。");
                if (loader == null || !loader.HasBody)
                    return new Analysis(false, "文件结构不匹配：未找到下载页初始化方法。");
                if (loaderPatched)
                    return new Analysis(false, "该文件已经是解锁状态，无需重复处理。");

                try
                {
                    DryRun(asm);
                }
                catch (Exception ex)
                {
                    return new Analysis(false, "暂不支持该版本（" + version + "）：" + ex.Message);
                }

                return new Analysis(true,
                    "检测到 PCL " + version + "，结构匹配，可以解锁（将直接覆盖原文件）。");
            }
            finally
            {
                asm.Dispose();
            }
        }

        static void DryRun(AssemblyDefinition asm)
        {
            ModuleDefinition mod = asm.MainModule;
            RefIndex refs = RefIndex.Build(mod);
            PatchLoaderInit(mod);
            PatchModpackInstallEntry(mod, refs);
            PatchModpackInstallDispatch(mod, refs);
            PatchModifyCardVisibility(mod);
            PatchResourceDetailInstallButton(mod);
        }

        // ---------------- patch ----------------

        public static void PatchInPlace(string path)
        {
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            string temp = Path.Combine(dir, Path.GetFileName(full) + ".unlock.tmp");

            Patch(full, temp);

            try
            {
                File.Replace(temp, full, null);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
                throw new Exception(
                    "无法覆盖原文件：它可能正在运行或被占用，或当前目录没有写入权限。\r\n请关闭 PCL 后重试。", ex);
            }
        }

        public static void Patch(string input, string output)
        {
            string temp = Path.Combine(Path.GetTempPath(), "PclUnlock_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(input));
                DefaultAssemblyResolver resolver = new DefaultAssemblyResolver();
                resolver.AddSearchDirectory(dir);

                AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(input,
                    new ReaderParameters
                    {
                        AssemblyResolver = resolver,
                        InMemory = true,
                        ReadingMode = ReadingMode.Immediate
                    });
                try
                {
                    ModuleDefinition mod = asm.MainModule;
                    ExtractEmbeddedAssemblies(mod, temp);
                    resolver.AddSearchDirectory(temp);

                    ValidateBeforePatch(asm);

                    RefIndex refs = RefIndex.Build(mod);
                    PatchLoaderInit(mod);
                    PatchModpackInstallEntry(mod, refs);
                    PatchModpackInstallDispatch(mod, refs);
                    PatchModifyCardVisibility(mod);
                    PatchResourceDetailInstallButton(mod);

                    if (File.Exists(output)) File.Delete(output);
                    asm.Write(output);
                }
                finally
                {
                    asm.Dispose();
                }

                VerifyOutput(output);
            }
            catch
            {
                try { if (File.Exists(output)) File.Delete(output); } catch { }
                throw;
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        static void ValidateBeforePatch(AssemblyDefinition asm)
        {
            ModuleDefinition mod = asm.MainModule;
            if (asm.Name.Name != "Plain Craft Launcher 2")
                throw new Exception("这不是 PCL 主程序。");

            TypeDefinition secret = FindTypeOrNull(mod, "PCL.ModSecret");
            FieldDefinition restrict = null;
            if (secret != null)
                restrict = secret.Fields.FirstOrDefault(f => f.Name == "RestrictMcDownload");
            if (restrict == null || !(restrict.Constant is bool) || !(bool)restrict.Constant)
                throw new Exception("该文件没有启用下载限制，无需解锁。");

            TypeDefinition mm = FindType(mod, "PCL.ModModpack");
            MethodDefinition m3 = FindMethod(mm, "ModpackInstall", 3);
            if (FindRestrictionThrow(m3) == null)
                throw new Exception("未找到整合包安装限制逻辑（可能已解锁或版本不兼容）。");

            TypeDefinition page = FindType(mod, "PCL.PageDownloadInstall");
            MethodDefinition loader = FindMethod(page, "LoaderInit", 0);
            if (FindDlLoaderInMethod(loader) != null)
                throw new Exception("该文件似乎已经是解锁版，无需重复处理。");
        }

        static void VerifyOutput(string path)
        {
            AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(path,
                new ReaderParameters { InMemory = true, ReadingMode = ReadingMode.Immediate });
            try
            {
                ModuleDefinition mod = asm.MainModule;
                TypeDefinition mm = FindType(mod, "PCL.ModModpack");
                MethodDefinition m3 = FindMethod(mm, "ModpackInstall", 3);
                string[] installNames = { "InstallPackCurseForge", "InstallPackHMCL", "InstallPackMMC",
                                          "InstallPackMCBBS", "InstallPackModrinth", "InstallPackCompress",
                                          "InstallPackLauncherPack" };
                foreach (string name in installNames)
                {
                    Instruction call = null;
                    foreach (Instruction i in m3.Body.Instructions)
                    {
                        MethodReference mr = i.Operand as MethodReference;
                        if (i.OpCode == OpCodes.Call && mr != null && mr.Name == name) { call = i; break; }
                    }
                    if (call == null) throw new Exception("写入校验失败：未找到 " + name + " 调用。");
                    VerifyCallArguments(call, name);
                }

                TypeDefinition page = FindType(mod, "PCL.PageDownloadInstall");
                MethodDefinition loader = FindMethod(page, "LoaderInit", 0);
                if (FindDlLoaderInMethod(loader) == null)
                    throw new Exception("写入校验失败：下载加载器未恢复。");

                TypeDefinition modify = FindType(mod, "PCL.PageDownloadModify");
                MethodDefinition lif = FindMethod(modify, "LoadInstanceFinished", 1);
                bool visible = false;
                IList<Instruction> ins = lif.Body.Instructions;
                for (int i = 0; i + 2 < ins.Count; i++)
                {
                    MethodReference mr = ins[i].Operand as MethodReference;
                    if (mr != null && mr.Name == "get_CardMinecraft" &&
                        ins[i + 1].OpCode == OpCodes.Ldc_I4_0)
                    {
                        MethodReference setv = ins[i + 2].Operand as MethodReference;
                        if (setv != null && setv.Name == "set_Visibility") { visible = true; break; }
                    }
                }
                if (!visible) throw new Exception("写入校验失败：MC 版本卡片未恢复显示。");

                TypeDefinition rd = FindType(mod, "PCL.PageDownloadResourceDetail");
                MethodDefinition init = FindMethod(rd, "InitializeStackResourceVersions", 1);
                bool hasInstall = init.Body.Instructions.Any(i =>
                {
                    MethodReference mr = i.Operand as MethodReference;
                    return i.OpCode == OpCodes.Ldftn && mr != null && mr.Name == "Install_Click";
                });
                if (!hasInstall) throw new Exception("写入校验失败：资源安装按钮未恢复。");
            }
            finally
            {
                asm.Dispose();
            }
        }

        // 按生成规则核对传给安装方法的参数个数，防止出现栈不平衡的非法 IL。
        static void VerifyCallArguments(Instruction call, string name)
        {
            MethodReference mr = call.Operand as MethodReference;
            if (mr == null) throw new Exception("写入校验失败：" + name + " 调用无效。");
            int n = mr.Parameters.Count;
            if (n < 2 || n > 5) throw new Exception("写入校验失败：" + name + " 参数数量异常。");

            Instruction p = call.Previous;
            if (n == 5 && (p == null || p.OpCode != OpCodes.Ldarg_2)) throw new Exception("写入校验失败：" + name + " 参数不完整。");
            if (n >= 5) p = p.Previous;
            if (n == 4 && (p == null || p.OpCode != OpCodes.Ldarg_1)) throw new Exception("写入校验失败：" + name + " 参数不完整。");
            if (n >= 4) p = p.Previous;
            if (n >= 3 && (p == null || p.OpCode != OpCodes.Ldloc && p.OpCode != OpCodes.Ldloc_S))
                throw new Exception("写入校验失败：" + name + " 参数不完整。");
            if (n >= 3) p = p.Previous;
            if (p == null || (p.OpCode != OpCodes.Ldloc && p.OpCode != OpCodes.Ldloc_S))
                throw new Exception("写入校验失败：" + name + " 参数不完整。");
            p = p.Previous;
            if (p == null || p.OpCode != OpCodes.Ldarg_0)
                throw new Exception("写入校验失败：" + name + " 参数不完整。");
        }

        // ---------------- embedded dependencies ----------------

        static void ExtractEmbeddedAssemblies(ModuleDefinition mod, string dir)
        {
            foreach (Resource r in mod.Resources)
            {
                EmbeddedResource er = r as EmbeddedResource;
                if (er == null || !er.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    continue;

                byte[] data;
                using (Stream s = er.GetResourceStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }

                using (MemoryStream ms = new MemoryStream(data))
                using (ResourceReader reader = new ResourceReader(ms))
                {
                    System.Collections.IDictionaryEnumerator e = reader.GetEnumerator();
                    while (e.MoveNext())
                    {
                        string key = e.Key as string;
                        byte[] val = e.Value as byte[];
                        if (key == null || val == null || val.Length < 2) continue;
                        if (val[0] != (byte)'M' || val[1] != (byte)'Z') continue;

                        string name = key;
                        if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            name += ".dll";
                        File.WriteAllBytes(Path.Combine(dir, name), val);
                    }
                }
            }
        }

        // ---------------- patch 1 ----------------

        static void PatchLoaderInit(ModuleDefinition mod)
        {
            TypeDefinition t = FindType(mod, "PCL.PageDownloadInstall");
            MethodDefinition m = FindMethod(t, "LoaderInit", 0);
            if (!m.HasBody) throw new Exception("LoaderInit 没有方法体。");

            MethodReference controlInit = null, getLoad = null, getPanLoad = null,
                            getPanAllBack = null, pageLoaderInit = null;
            Instruction callIns = null;
            foreach (Instruction i in m.Body.Instructions)
            {
                MethodReference mr = i.Operand as MethodReference;
                if (mr == null) continue;
                if (mr.Name == "ControlInit") controlInit = mr;
                else if (mr.Name == "get_LoadMinecraft") getLoad = mr;
                else if (mr.Name == "get_PanLoad") getPanLoad = mr;
                else if (mr.Name == "get_PanAllBack") getPanAllBack = mr;
                else if (mr.Name == "PageLoaderInit") { pageLoaderInit = mr; callIns = i; }
            }

            if (callIns == null)
                throw new Exception("下载页初始化结构与预期不符（未找到 PageLoaderInit 调用）。");

            // 从 PageLoaderInit 调用向前找到回调委托（最后一个 Action`1 构造），不依赖 lambda 名称。
            MethodDefinition callback = null;
            MethodReference actionCtor = null;
            Instruction p = callIns.Previous;
            int steps = 0;
            while (p != null && steps++ < 12)
            {
                if (p.OpCode == OpCodes.Newobj)
                {
                    MethodReference ctor = p.Operand as MethodReference;
                    if (ctor != null && ctor.DeclaringType.FullName.StartsWith("System.Action`1", StringComparison.Ordinal))
                    {
                        Instruction q = p.Previous;
                        if (q != null && q.OpCode == OpCodes.Ldftn)
                        {
                            MethodReference target = q.Operand as MethodReference;
                            if (target != null)
                            {
                                actionCtor = ctor;
                                callback = target as MethodDefinition;
                                if (callback == null)
                                {
                                    try { callback = target.Resolve(); } catch { }
                                }
                            }
                        }
                        break;
                    }
                }
                p = p.Previous;
            }

            FieldReference dlLoader = FindDlLoaderInType(t);

            if (controlInit == null || getLoad == null || getPanLoad == null || getPanAllBack == null ||
                actionCtor == null || pageLoaderInit == null || callback == null || dlLoader == null)
                throw new Exception("下载页初始化结构不匹配，已停止。");

            MethodBody body = new MethodBody(m);
            ILProcessor il = body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, controlInit);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getLoad);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getPanLoad);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, getPanAllBack);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldsfld, dlLoader);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldftn, callback);
            il.Emit(OpCodes.Newobj, actionCtor);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, pageLoaderInit);
            il.Emit(OpCodes.Ret);
            m.Body = body;
        }

        // ---------------- patch 2 ----------------

        static void PatchModpackInstallEntry(ModuleDefinition mod, RefIndex refs)
        {
            TypeDefinition t = FindType(mod, "PCL.ModModpack");
            MethodDefinition m = FindMethod(t, "ModpackInstall", 0);
            if (!m.HasBody) throw new Exception("ModpackInstall() 没有方法体。");

            // 从原方法开头的 newobj 识别闭包类型，不依赖自动生成的类型名。
            TypeDefinition closure = null;
            foreach (Instruction i in m.Body.Instructions)
            {
                if (i.OpCode != OpCodes.Newobj) continue;
                MethodReference mr = i.Operand as MethodReference;
                if (mr == null) continue;
                TypeReference tr = mr.DeclaringType;
                if (tr == null || tr.FullName == null) continue;
                if (tr.FullName.StartsWith(t.FullName + "/", StringComparison.Ordinal))
                {
                    closure = tr as TypeDefinition;
                    if (closure == null)
                    {
                        try { closure = tr.Resolve(); } catch { }
                    }
                    if (closure != null) break;
                }
            }
            if (closure == null) throw new Exception("未找到整合包安装的闭包类型。");

            MethodDefinition ctor = closure.Methods.FirstOrDefault(x => x.IsConstructor && x.Parameters.Count == 0);
            FieldDefinition field = closure.Fields.FirstOrDefault(x => !x.IsStatic && x.Name == "$VB$Local_File")
                                 ?? closure.Fields.FirstOrDefault(x => !x.IsStatic && x.FieldType.FullName == "System.String");
            MethodDefinition lambda = closure.Methods.FirstOrDefault(x => !x.IsStatic && !x.IsConstructor &&
                                                                          x.Name.StartsWith("_Lambda$__", StringComparison.Ordinal) &&
                                                                          x.Parameters.Count == 0 && x.ReturnType.FullName == "System.Void")
                                 ?? closure.Methods.FirstOrDefault(x => !x.IsStatic && !x.IsConstructor &&
                                                                          x.Parameters.Count == 0 && x.ReturnType.FullName == "System.Void");

            TypeDefinition baseType = FindType(mod, "PCL.ModBase");
            MethodDefinition run = FindMethod(baseType, "RunInThread", 1);

            if (ctor == null || field == null || lambda == null)
                throw new Exception("整合包安装闭包结构不匹配，已停止。");

            MethodReference select = refs.Find(mr =>
                mr.DeclaringType.FullName == "MeloongCore.Wpf.Dialogs" &&
                mr.Name == "SelectFile" && mr.Parameters.Count == 4);
            if (select == null) throw new Exception("未找到 SelectFile 引用。");

            GenericInstanceType nullableType = select.Parameters[3].ParameterType as GenericInstanceType;
            if (nullableType == null) throw new Exception("SelectFile 参数结构不匹配。");
            GenericInstanceType oneOf = nullableType.GenericArguments[0] as GenericInstanceType;
            if (oneOf == null) throw new Exception("SelectFile 参数结构不匹配。");
            GenericInstanceType enumerable = oneOf.GenericArguments[0] as GenericInstanceType;
            if (enumerable == null) throw new Exception("SelectFile 参数结构不匹配。");
            GenericInstanceType valueTuple = enumerable.GenericArguments[0] as GenericInstanceType;
            if (valueTuple == null) throw new Exception("SelectFile 参数结构不匹配。");

            // 传入的是 (扩展名, 描述) 数组，必须使用 OneOf 的 !0（可枚举）隐式转换。
            MethodReference implicitRef = refs.Find(mr =>
                mr.Name == "op_Implicit" && mr.DeclaringType.FullName == oneOf.FullName &&
                mr.Parameters.Count == 1 && IsGenericParamAt(mr.Parameters[0].ParameterType, 0));
            MethodReference nullableCtor = refs.Find(mr =>
                mr.Name == ".ctor" && mr.DeclaringType.FullName == nullableType.FullName &&
                mr.Parameters.Count == 1);
            MethodReference first = refs.Find(mr =>
            {
                if (mr.Name != "FirstOrDefault" || mr.DeclaringType.FullName != "System.Linq.Enumerable" ||
                    mr.Parameters.Count != 1) return false;
                GenericInstanceMethod gim = mr as GenericInstanceMethod;
                return gim != null && gim.GenericArguments.Count == 1 &&
                       gim.GenericArguments[0].FullName == "System.String";
            });
            MethodReference vtCtor = refs.Find(mr =>
                mr.Name == ".ctor" && mr.DeclaringType.FullName == valueTuple.FullName &&
                mr.Parameters.Count == 2);
            if (vtCtor == null)
            {
                MethodReference c = new MethodReference(".ctor", mod.TypeSystem.Void, valueTuple);
                c.HasThis = true;
                c.Parameters.Add(new ParameterDefinition(valueTuple.GenericArguments[0]));
                c.Parameters.Add(new ParameterDefinition(valueTuple.GenericArguments[1]));
                vtCtor = c;
            }
            if (implicitRef == null || nullableCtor == null || first == null)
                throw new Exception("文件选择相关引用不完整，已停止。");

            MethodReference isNull = mod.ImportReference(
                typeof(string).GetMethod("IsNullOrEmpty", new Type[] { typeof(string) }));
            MethodReference actionCtor = mod.ImportReference(
                typeof(Action).GetConstructor(new Type[] { typeof(object), typeof(IntPtr) }));

            MethodBody body = new MethodBody(m);
            body.InitLocals = true;
            body.Variables.Add(new VariableDefinition(closure));
            ILProcessor il = body.GetILProcessor();

            il.Emit(OpCodes.Newobj, ctor);
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldstr, "选择整合包压缩文件");
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newarr, valueTuple);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldc_I4_3);
            il.Emit(OpCodes.Newarr, mod.TypeSystem.String);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldstr, "rar"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ldstr, "zip"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldstr, "mrpack"); il.Emit(OpCodes.Stelem_Ref);
            il.Emit(OpCodes.Ldstr, "整合包文件");
            il.Emit(OpCodes.Newobj, vtCtor);
            il.Emit(OpCodes.Stelem_Any, valueTuple);
            il.Emit(OpCodes.Call, implicitRef);
            il.Emit(OpCodes.Newobj, nullableCtor);
            il.Emit(OpCodes.Call, select);
            il.Emit(OpCodes.Call, first);
            il.Emit(OpCodes.Stfld, field);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Call, isNull);

            Instruction retIns = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Brtrue, retIns);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ldftn, lambda);
            il.Emit(OpCodes.Newobj, actionCtor);
            il.Emit(OpCodes.Call, run);
            body.Instructions.Add(retIns);

            m.Body = body;
        }

        // ---------------- patch 3 ----------------

        static void PatchModpackInstallDispatch(ModuleDefinition mod, RefIndex refs)
        {
            TypeDefinition t = FindType(mod, "PCL.ModModpack");
            MethodDefinition m = FindMethod(t, "ModpackInstall", 3);
            MethodBody body = m.Body;

            Instruction at = FindRestrictionThrow(m);
            if (at == null) throw new Exception("未找到整合包安装限制逻辑（可能已解锁或版本不兼容）。");
            Instruction at2 = at.Next;
            Instruction at3 = at2 == null ? null : at2.Next;
            if (at2 == null || at3 == null || at2.OpCode != OpCodes.Newobj || at3.OpCode != OpCodes.Throw)
                throw new Exception("整合包分发结构与预期不符，已停止。");

            VariableDefinition kind = FindBranchStoredLocal(m, at);
            if (kind == null || kind.VariableType.FullName != "System.Int32")
                throw new Exception("未能确定整合包类型变量，已停止。");

            VariableDefinition archive = FindSingleLocal(body, "System.IO.Compression.ZipArchive", "整合包压缩对象");
            VariableDefinition baseFolder = FindBaseFolderLocal(body);
            VariableDefinition result = FindLoaderComboLocal(body);

            ExceptionHandler outerFinally = null;
            foreach (ExceptionHandler h in body.ExceptionHandlers)
            {
                if (h.HandlerType != ExceptionHandlerType.Finally) continue;
                if (!(h.TryStart.Offset <= at.Offset && at.Offset < h.TryEnd.Offset)) continue;
                if (outerFinally == null || h.TryStart.Offset < outerFinally.TryStart.Offset)
                    outerFinally = h;
            }
            if (outerFinally == null) throw new Exception("未找到整合包方法的异常处理结构。");

            MethodReference info = refs.Find(mr =>
                mr.DeclaringType.FullName == "MeloongCore.Logger" && mr.Name == "Info" &&
                mr.Parameters.Count == 3 && mr.Parameters[0].ParameterType.FullName == "System.String");
            if (info == null) throw new Exception("未找到日志方法引用。");

            MethodDefinition curse = FindMethodAny(t, "InstallPackCurseForge");
            MethodDefinition hmcl = FindMethodAny(t, "InstallPackHMCL");
            MethodDefinition mmc = FindMethodAny(t, "InstallPackMMC");
            MethodDefinition mcbbs = FindMethodAny(t, "InstallPackMCBBS");
            MethodDefinition modrinth = FindMethodAny(t, "InstallPackModrinth");
            MethodDefinition launcher = FindMethodAny(t, "InstallPackLauncherPack");
            MethodDefinition compress = FindMethodAny(t, "InstallPackCompress");

            Instruction endIns = Instruction.Create(OpCodes.Ldloc, result);
            Instruction retIns = Instruction.Create(OpCodes.Ret);

            Instruction labelDefault = Instruction.Create(OpCodes.Ldstr, "整合包种类：未能识别，假定为压缩包");
            Instruction labelCurse = Instruction.Create(OpCodes.Ldstr, "整合包种类：CurseForge");
            Instruction labelHmcl = Instruction.Create(OpCodes.Ldstr, "整合包种类：HMCL");
            Instruction labelMmc = Instruction.Create(OpCodes.Ldstr, "整合包种类：MMC");
            Instruction labelMcbbs = Instruction.Create(OpCodes.Ldstr, "整合包种类：MCBBS");
            Instruction labelModrinth = Instruction.Create(OpCodes.Ldstr, "整合包种类：Modrinth");
            Instruction labelLauncher = Instruction.Create(OpCodes.Ldstr, "整合包种类：带启动器的压缩包");

            Instruction[] targets = new Instruction[]
            {
                labelCurse, labelHmcl, labelMmc, labelMcbbs, labelModrinth,
                labelDefault, labelDefault, labelDefault, labelDefault, labelLauncher
            };

            List<Instruction> rest = new List<Instruction>();
            rest.Add(Instruction.Create(OpCodes.Br, labelDefault));
            AddCase(rest, labelCurse, "整合包种类：CurseForge", info, result, archive, baseFolder, curse, endIns);
            AddCase(rest, labelHmcl, "整合包种类：HMCL", info, result, archive, baseFolder, hmcl, endIns);
            AddCase(rest, labelMmc, "整合包种类：MMC", info, result, archive, baseFolder, mmc, endIns);
            AddCase(rest, labelMcbbs, "整合包种类：MCBBS", info, result, archive, baseFolder, mcbbs, endIns);
            AddCase(rest, labelModrinth, "整合包种类：Modrinth", info, result, archive, baseFolder, modrinth, endIns);
            AddCase(rest, labelLauncher, "整合包种类：带启动器的压缩包", info, result, archive, baseFolder, launcher, endIns);
            AddCase(rest, labelDefault, "整合包种类：未能识别，假定为压缩包", info, result, archive, baseFolder, compress, endIns);

            at.OpCode = OpCodes.Ldloc_S;
            at.Operand = kind;
            at2.OpCode = OpCodes.Switch;
            at2.Operand = targets;

            int index = body.Instructions.IndexOf(at2);
            foreach (Instruction ins in rest)
            {
                index++;
                body.Instructions.Insert(index, ins);
            }

            body.Instructions.Remove(at3);
            body.Instructions.Add(endIns);
            body.Instructions.Add(retIns);
            outerFinally.HandlerEnd = endIns;
        }

        static void AddCase(List<Instruction> list, Instruction label, string text,
                            MethodReference info, VariableDefinition result,
                            VariableDefinition archive, VariableDefinition baseFolder,
                            MethodDefinition target, Instruction endIns)
        {
            int n = target.Parameters.Count;
            if (n < 2 || n > 5)
                throw new Exception("安装方法参数数量异常：" + target.Name + "/" + n + "，已停止。");

            list.Add(label);
            list.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            list.Add(Instruction.Create(OpCodes.Ldstr, "ModModpack.vb"));
            list.Add(Instruction.Create(OpCodes.Call, info));

            list.Add(Instruction.Create(OpCodes.Ldarg_0));
            list.Add(Instruction.Create(OpCodes.Ldloc, archive));
            if (n >= 3) list.Add(Instruction.Create(OpCodes.Ldloc, baseFolder));
            if (n >= 4) list.Add(Instruction.Create(OpCodes.Ldarg_1));
            if (n >= 5) list.Add(Instruction.Create(OpCodes.Ldarg_2));

            list.Add(Instruction.Create(OpCodes.Call, target));
            list.Add(Instruction.Create(OpCodes.Stloc, result));
            list.Add(Instruction.Create(OpCodes.Leave, endIns));
        }

        // ---------------- patch 4 ----------------

        static void PatchModifyCardVisibility(ModuleDefinition mod)
        {
            TypeDefinition t = FindType(mod, "PCL.PageDownloadModify");
            MethodDefinition m = FindMethod(t, "LoadInstanceFinished", 1);
            IList<Instruction> list = m.Body.Instructions;

            for (int i = 0; i + 2 < list.Count; i++)
            {
                MethodReference mr = list[i].Operand as MethodReference;
                if (mr == null || mr.Name != "get_CardMinecraft") continue;

                Instruction ldc = list[i + 1];
                MethodReference setv = list[i + 2].Operand as MethodReference;
                if (setv != null && setv.Name == "set_Visibility" && ldc.OpCode == OpCodes.Ldc_I4_2)
                {
                    ldc.OpCode = OpCodes.Ldc_I4_0;
                    return;
                }
            }
            throw new Exception("未找到 MC 版本卡片显示逻辑，已停止。");
        }

        // ---------------- patch 5 ----------------

        static void PatchResourceDetailInstallButton(ModuleDefinition mod)
        {
            TypeDefinition t = FindType(mod, "PCL.PageDownloadResourceDetail");
            MethodDefinition m = FindMethod(t, "InitializeStackResourceVersions", 1);
            MethodBody body = m.Body;

            MethodDefinition saveClick = FindMethod(t, "Save_Click", 2);
            MethodDefinition installClick = FindMethod(t, "Install_Click", 2);

            IList<Instruction> list = body.Instructions;
            Instruction saveLdftn = null, toListCall = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].OpCode != OpCodes.Ldftn) continue;
                MethodReference mr = list[i].Operand as MethodReference;
                if (mr == null || mr.Name != saveClick.Name || mr.DeclaringType.FullName != t.FullName) continue;
                for (int j = i + 1; j < Math.Min(list.Count, i + 9); j++)
                {
                    MethodReference tm = list[j].Operand as MethodReference;
                    if (list[j].OpCode == OpCodes.Callvirt && tm != null && tm.Name == "ToListItem")
                    {
                        saveLdftn = list[i];
                        toListCall = list[j];
                        break;
                    }
                }
                if (toListCall != null) break;
            }
            if (toListCall == null) throw new Exception("未找到资源版本列表构建逻辑，已停止。");

            Instruction p3 = saveLdftn.Previous;
            Instruction p2 = p3 == null ? null : p3.Previous;
            Instruction p1 = p2 == null ? null : p2.Previous;
            Instruction p0 = p1 == null ? null : p1.Previous;

            if (p0 == null || p0.OpCode != OpCodes.Ldarg_1 ||
                p1 == null || (p1.OpCode != OpCodes.Callvirt && p1.OpCode != OpCodes.Call) ||
                p2 == null || p3 == null || p3.OpCode != OpCodes.Ldarg_0)
                throw new Exception("资源版本列表结构不匹配，已停止。");

            MethodReference getChildren = p1.Operand as MethodReference;
            if (getChildren == null || getChildren.Name != "get_Children")
                throw new Exception("资源版本列表结构不匹配，已停止。");

            VariableDefinition itemVar = LoadedVariable(p2, body);
            MethodReference clickCtor = null;
            Instruction p4 = saveLdftn.Next;
            if (p4 != null && p4.OpCode == OpCodes.Newobj)
                clickCtor = p4.Operand as MethodReference;
            Instruction p7 = toListCall.Previous;
            VariableDefinition boolVar = LoadedVariable(p7, body);

            Instruction addCall = toListCall.Next;
            Instruction pop = addCall == null ? null : addCall.Next;
            MethodReference addMethod = addCall == null ? null : addCall.Operand as MethodReference;

            if (itemVar == null || boolVar == null || boolVar.VariableType.FullName != "System.Boolean" ||
                clickCtor == null || clickCtor.DeclaringType.Name != "ClickEventHandler" ||
                addMethod == null || addMethod.Name != "Add" || pop == null || pop.OpCode != OpCodes.Pop)
                throw new Exception("资源版本列表结构不匹配，已停止。");

            FieldDefinition targetField = t.Fields.FirstOrDefault(f => f.Name == "TargetResourceType");
            if (targetField == null) throw new Exception("未找到资源类型字段，已停止。");

            Instruction after = pop.Next;
            if (after == null) throw new Exception("资源版本列表结构不匹配，已停止。");

            List<Instruction> remove = new List<Instruction>();
            Instruction cur = p0;
            while (true)
            {
                remove.Add(cur);
                if (cur == pop) break;
                cur = cur.Next;
                if (cur == null) throw new Exception("资源版本列表结构不匹配，已停止。");
            }
            foreach (Instruction i in remove) body.Instructions.Remove(i);

            Instruction ifLabel = Instruction.Create(OpCodes.Nop);
            Instruction endLabel = Instruction.Create(OpCodes.Nop);
            Instruction beq = Instruction.Create(OpCodes.Beq, ifLabel);
            Instruction br = Instruction.Create(OpCodes.Br, endLabel);

            List<Instruction> ins = new List<Instruction>();
            ins.Add(Instruction.Create(OpCodes.Ldarg_1));
            ins.Add(Instruction.Create(OpCodes.Callvirt, getChildren));
            ins.Add(Instruction.Create(OpCodes.Ldarg_0));
            ins.Add(Instruction.Create(OpCodes.Ldfld, targetField));
            ins.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            ins.Add(beq);

            ins.Add(Instruction.Create(OpCodes.Ldloc, itemVar));
            ins.Add(Instruction.Create(OpCodes.Ldarg_0));
            ins.Add(Instruction.Create(OpCodes.Ldftn, saveClick));
            ins.Add(Instruction.Create(OpCodes.Newobj, clickCtor));
            ins.Add(Instruction.Create(OpCodes.Ldnull));
            ins.Add(Instruction.Create(OpCodes.Ldloc, boolVar));
            ins.Add(Instruction.Create(OpCodes.Callvirt, toListCall.Operand as MethodReference));
            ins.Add(br);

            ins.Add(ifLabel);
            ins.Add(Instruction.Create(OpCodes.Ldloc, itemVar));
            ins.Add(Instruction.Create(OpCodes.Ldarg_0));
            ins.Add(Instruction.Create(OpCodes.Ldftn, installClick));
            ins.Add(Instruction.Create(OpCodes.Newobj, clickCtor));
            ins.Add(Instruction.Create(OpCodes.Ldarg_0));
            ins.Add(Instruction.Create(OpCodes.Ldftn, saveClick));
            ins.Add(Instruction.Create(OpCodes.Newobj, clickCtor));
            ins.Add(Instruction.Create(OpCodes.Ldloc, boolVar));
            ins.Add(Instruction.Create(OpCodes.Callvirt, toListCall.Operand as MethodReference));

            ins.Add(endLabel);
            ins.Add(Instruction.Create(OpCodes.Callvirt, addMethod));
            ins.Add(Instruction.Create(OpCodes.Pop));

            int index = body.Instructions.IndexOf(after);
            foreach (Instruction i in ins)
            {
                body.Instructions.Insert(index, i);
                index++;
            }
        }

        // ---------------- locating helpers ----------------

        // 限制语句编译后固定为：ldstr <提示>; newobj Exception::.ctor(string); throw
        static Instruction FindRestrictionThrow(MethodDefinition m)
        {
            if (m == null || !m.HasBody) return null;
            foreach (Instruction i in m.Body.Instructions)
            {
                if (i.OpCode != OpCodes.Throw) continue;
                Instruction prev = i.Previous;
                if (prev == null || prev.OpCode != OpCodes.Newobj) continue;
                MethodReference ctor = prev.Operand as MethodReference;
                if (ctor == null || ctor.Name != ".ctor" || ctor.Parameters.Count != 1) continue;
                if (ctor.DeclaringType.FullName != "System.Exception") continue;
                Instruction prev2 = prev.Previous;
                if (prev2 == null || prev2.OpCode != OpCodes.Ldstr) continue;
                return prev2;
            }
            return null;
        }

        static bool HasDispatch(MethodDefinition m)
        {
            bool hasSwitch = false, hasInstall = false;
            foreach (Instruction i in m.Body.Instructions)
            {
                if (i.OpCode == OpCodes.Switch) hasSwitch = true;
                MethodReference mr = i.Operand as MethodReference;
                if (mr != null && mr.Name.StartsWith("InstallPack", StringComparison.Ordinal)) hasInstall = true;
            }
            return hasSwitch && hasInstall;
        }

        static FieldReference FindDlLoaderInMethod(MethodDefinition m)
        {
            if (m == null || !m.HasBody) return null;
            foreach (Instruction i in m.Body.Instructions)
            {
                if (i.OpCode != OpCodes.Ldsfld) continue;
                FieldReference fr = i.Operand as FieldReference;
                if (fr == null) continue;
                if (fr.FieldType.FullName.StartsWith("PCL.ModLoader/LoaderTask`2<System.String",
                                                     StringComparison.Ordinal)) return fr;
            }
            return null;
        }

        static FieldReference FindDlLoaderInType(TypeDefinition t)
        {
            if (t == null) return null;
            FieldReference fallback = null;
            foreach (MethodDefinition m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (Instruction i in m.Body.Instructions)
                {
                    if (i.OpCode != OpCodes.Ldsfld) continue;
                    FieldReference fr = i.Operand as FieldReference;
                    if (fr == null) continue;
                    if (!fr.FieldType.FullName.StartsWith("PCL.ModLoader/LoaderTask`2<System.String",
                                                         StringComparison.Ordinal)) continue;
                    if (fr.Name == "DlClientListLoader") return fr;
                    if (fallback == null) fallback = fr;
                }
            }
            return fallback;
        }

        // 找到所有跳向限制语句的分支，取分支前被写入的局部变量，即整合包类型。
        static VariableDefinition FindBranchStoredLocal(MethodDefinition m, Instruction target)
        {
            VariableDefinition found = null;
            foreach (Instruction i in m.Body.Instructions)
            {
                if (i.Operand != target) continue;
                if (!IsBranch(i.OpCode)) continue;
                VariableDefinition v = StoredVariable(i.Previous, m.Body);
                if (v == null || v.VariableType.FullName != "System.Int32") continue;
                if (found != null && found != v)
                    throw new Exception("整合包类型变量的使用方式与预期不符。");
                found = v;
            }
            return found;
        }

        static bool IsBranch(OpCode op)
        {
            return op == OpCodes.Br || op == OpCodes.Br_S ||
                   op == OpCodes.Leave || op == OpCodes.Leave_S;
        }

        static VariableDefinition StoredVariable(Instruction i, MethodBody body)
        {
            if (i == null) return null;
            switch (i.OpCode.Code)
            {
                case Code.Stloc_0: return body.Variables.Count > 0 ? body.Variables[0] : null;
                case Code.Stloc_1: return body.Variables.Count > 1 ? body.Variables[1] : null;
                case Code.Stloc_2: return body.Variables.Count > 2 ? body.Variables[2] : null;
                case Code.Stloc_3: return body.Variables.Count > 3 ? body.Variables[3] : null;
                case Code.Stloc_S:
                case Code.Stloc: return i.Operand as VariableDefinition;
                default: return null;
            }
        }

        static VariableDefinition LoadedVariable(Instruction i, MethodBody body)
        {
            if (i == null) return null;
            switch (i.OpCode.Code)
            {
                case Code.Ldloc_0: return body.Variables.Count > 0 ? body.Variables[0] : null;
                case Code.Ldloc_1: return body.Variables.Count > 1 ? body.Variables[1] : null;
                case Code.Ldloc_2: return body.Variables.Count > 2 ? body.Variables[2] : null;
                case Code.Ldloc_3: return body.Variables.Count > 3 ? body.Variables[3] : null;
                case Code.Ldloc_S:
                case Code.Ldloc: return i.Operand as VariableDefinition;
                default: return null;
            }
        }

        static VariableDefinition FindSingleLocal(MethodBody body, string typeName, string what)
        {
            List<VariableDefinition> list = body.Variables
                .Where(v => v.VariableType.FullName == typeName).ToList();
            if (list.Count != 1)
                throw new Exception("未能唯一定位" + what + "，已停止。");
            return list[0];
        }

        static VariableDefinition FindBaseFolderLocal(MethodBody body)
        {
            foreach (Instruction i in body.Instructions)
            {
                if (i.OpCode != OpCodes.Ldstr || (i.Operand as string) != "overrides/") continue;
                VariableDefinition v = StoredVariable(i.Next, body);
                if (v != null && v.VariableType.FullName == "System.String") return v;
            }
            foreach (Instruction i in body.Instructions)
            {
                if (i.OpCode != OpCodes.Call) continue;
                MethodReference mr = i.Operand as MethodReference;
                if (mr == null || mr.Name != "Concat" || mr.DeclaringType.FullName != "System.String" ||
                    mr.Parameters.Count != 2) continue;
                if (i.Previous == null || i.Previous.OpCode != OpCodes.Ldstr ||
                    (i.Previous.Operand as string) != "/") continue;
                VariableDefinition v = StoredVariable(i.Next, body);
                if (v != null && v.VariableType.FullName == "System.String") return v;
            }
            throw new Exception("未能定位整合包基础目录变量，已停止。");
        }

        static VariableDefinition FindLoaderComboLocal(MethodBody body)
        {
            List<VariableDefinition> list = body.Variables
                .Where(v => v.VariableType.FullName.StartsWith("PCL.ModLoader/LoaderCombo`1<",
                                                               StringComparison.Ordinal)).ToList();
            if (list.Count != 1)
                throw new Exception("未能唯一定位安装结果变量，已停止。");
            return list[0];
        }

        static MethodDefinition FindMethodAny(TypeDefinition t, string name)
        {
            MethodDefinition m = t.Methods.FirstOrDefault(x => x.Name == name && !x.IsConstructor);
            if (m == null) throw new Exception("未找到安装方法：" + name);
            return m;
        }

        static bool IsGenericParamAt(TypeReference tr, int position)
        {
            GenericParameter gp = tr as GenericParameter;
            return gp != null && gp.Position == position;
        }

        // ---------------- generic helpers ----------------

        static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition m)
        {
            Stack<TypeDefinition> stack = new Stack<TypeDefinition>(m.Types);
            while (stack.Count > 0)
            {
                TypeDefinition t = stack.Pop();
                yield return t;
                foreach (TypeDefinition n in t.NestedTypes) stack.Push(n);
            }
        }

        static TypeDefinition FindTypeOrNull(ModuleDefinition m, string fullName)
        {
            foreach (TypeDefinition t in AllTypes(m))
                if (t.FullName == fullName) return t;
            return null;
        }

        static TypeDefinition FindType(ModuleDefinition m, string fullName)
        {
            TypeDefinition t = FindTypeOrNull(m, fullName);
            if (t == null) throw new Exception("未找到类型：" + fullName);
            return t;
        }

        static MethodDefinition FindMethodOrNull(TypeDefinition t, string name, int paramCount)
        {
            foreach (MethodDefinition m in t.Methods)
                if (m.Name == name && m.Parameters.Count == paramCount) return m;
            return null;
        }

        static MethodDefinition FindMethod(TypeDefinition t, string name, int paramCount)
        {
            MethodDefinition m = FindMethodOrNull(t, name, paramCount);
            if (m == null)
                throw new Exception("未找到方法：" + t.FullName + "::" + name + "/" + paramCount);
            return m;
        }
    }

    sealed class RefIndex
    {
        readonly List<MethodReference> all = new List<MethodReference>();
        readonly HashSet<string> seen = new HashSet<string>();

        public static RefIndex Build(ModuleDefinition mod)
        {
            RefIndex index = new RefIndex();
            foreach (TypeDefinition t in mod.GetTypes())
            {
                foreach (MethodDefinition m in t.Methods)
                {
                    if (!m.HasBody) continue;
                    foreach (Instruction i in m.Body.Instructions)
                    {
                        MethodReference mr = i.Operand as MethodReference;
                        if (mr == null) continue;
                        if (index.seen.Add(mr.FullName)) index.all.Add(mr);
                    }
                }
            }
            return index;
        }

        public MethodReference Find(Predicate<MethodReference> predicate)
        {
            foreach (MethodReference mr in all)
                if (predicate(mr)) return mr;
            return null;
        }
    }
}
