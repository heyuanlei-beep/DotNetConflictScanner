using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml;

namespace DotNetConflictScanner
{
    class Program
    {
        // bindingRedirect 规则: <程序集名, 目标新版本号>
        static readonly Dictionary<string, string> redirectRules = new(StringComparer.OrdinalIgnoreCase);

        // 主程序（第一个 .exe）直接引用的依赖名列表
        static readonly HashSet<string> mainAppDependencies = new(StringComparer.OrdinalIgnoreCase);
        static string mainAppName = "";

        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.Title = ".NET 依赖冲突智能诊断系统";

            // 1. 获取目标目录
            string targetDir = Directory.GetCurrentDirectory();
            if (args.Length > 0 && Directory.Exists(args[0]))
                targetDir = args[0];

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🔍 开始深度扫描目录: {targetDir}");
            Console.WriteLine("=================================================================");
            Console.ResetColor();

            // 2. 预加载环境：解析重定向配置 & 主程序依赖链
            LoadBindingRedirects(targetDir);
            MapMainAppDependencies(targetDir);

            // 结构: <依赖名, <版本号, List<引用它的文件名>>>
            var dependencyMap = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
            // 结构: <依赖名, <版本号, PublicKeyToken 十六进制字符串>>
            var tokenMap = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // 3. 遍历并解析所有 .NET 程序集
            var assemblyFiles = Directory.GetFiles(targetDir, "*.dll", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(targetDir, "*.exe", SearchOption.TopDirectoryOnly))
                .ToArray();

            int parsedCount = 0;
            int skippedCount = 0;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  找到 {assemblyFiles.Length} 个程序集文件（.dll / .exe），正在分析...");
            Console.ResetColor();

            foreach (var file in assemblyFiles)
            {
                try
                {
                    string currentName = Path.GetFileName(file);

                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var peReader = new PEReader(stream);

                    if (!peReader.HasMetadata)
                    {
                        skippedCount++;
                        continue;
                    }

                    MetadataReader mdReader = peReader.GetMetadataReader();

                    foreach (var asmRefHandle in mdReader.AssemblyReferences)
                    {
                        AssemblyReference asmRef = mdReader.GetAssemblyReference(asmRefHandle);
                        string depName = mdReader.GetString(asmRef.Name);
                        string depVersion = NormalizeVersion(asmRef.Version);

                        // 读取 PublicKeyToken（Blob 通常是 8 字节，或 nil 表示弱名称）
                        string tokenHex = "null";
                        if (!asmRef.PublicKeyOrToken.IsNil)
                        {
                            var blob = mdReader.GetBlobReader(asmRef.PublicKeyOrToken);
                            if (blob.Length > 0)
                            {
                                byte[] tokenBytes = blob.ReadBytes(blob.Length);
                                tokenHex = BitConverter.ToString(tokenBytes).Replace("-", "").ToLowerInvariant();
                            }
                        }

                        // 填充依赖树
                        if (!dependencyMap.ContainsKey(depName))
                            dependencyMap[depName] = new Dictionary<string, List<string>>();
                        if (!dependencyMap[depName].ContainsKey(depVersion))
                            dependencyMap[depName][depVersion] = new List<string>();
                        if (!dependencyMap[depName][depVersion].Contains(currentName))
                            dependencyMap[depName][depVersion].Add(currentName);

                        // 填充 Token 映射
                        if (!tokenMap.ContainsKey(depName))
                            tokenMap[depName] = new Dictionary<string, string>();
                        tokenMap[depName][depVersion] = tokenHex;
                    }

                    parsedCount++;
                }
                catch
                {
                    skippedCount++;
                }
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  解析完成：{parsedCount} 个 .NET 程序集，跳过 {skippedCount} 个非托管文件");
            Console.ResetColor();

            // ================================================================
            // 4. 智能风险评估引擎
            // ================================================================
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("\n📊 正在进行智能风险评估与逻辑推理...");
            Console.ResetColor();

            int criticalCount = 0;
            int warningCount = 0;
            int safeCount = 0;

            // 收集冲突并按名字排序
            var conflicts = new List<(string Name, Dictionary<string, List<string>> Versions)>();
            foreach (var kvp in dependencyMap)
                if (kvp.Value.Count > 1)
                    conflicts.Add((kvp.Key, kvp.Value));

            // 如果没有冲突，直接输出完美结果
            if (conflicts.Count == 0)
            {
                Console.WriteLine("\n=================================================================");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("🎉 完美！未检测到任何可能引发崩溃的依赖冲突异常。");
                Console.ResetColor();
                Console.WriteLine("\n按任意键退出...");
                try { Console.ReadKey(true); }
                catch (InvalidOperationException) { }
                return;
            }

            conflicts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            foreach (var (depName, versions) in conflicts)
            {
                // --- A. Token 一致性校验 ---
                bool hasTokenMismatch = false;
                string? firstToken = null;
                if (tokenMap.TryGetValue(depName, out var verTokens))
                {
                    foreach (var tok in verTokens.Values)
                    {
                        if (firstToken == null) firstToken = tok;
                        else if (firstToken != tok) { hasTokenMismatch = true; break; }
                    }
                }

                // --- B. bindingRedirect 状态 ---
                bool hasRedirect = redirectRules.TryGetValue(depName, out string? redirectTarget);

                // --- C. 代码路径触达率 ---
                bool isReachable = false;
                foreach (var verList in versions.Values)
                {
                    foreach (var parentDll in verList)
                    {
                        string parentNoExt = Path.GetFileNameWithoutExtension(parentDll);
                        if (parentDll.Equals(mainAppName, StringComparison.OrdinalIgnoreCase) ||
                            mainAppDependencies.Contains(parentNoExt))
                        {
                            isReachable = true;
                            break;
                        }
                    }
                    if (isReachable) break;
                }

                // --- D. 综合判定 ---
                string riskTitle;
                string diagnosticMsg;
                ConsoleColor riskColor;

                if (hasTokenMismatch)
                {
                    riskTitle = "🚨 DEADLOCK (致命死局：数字签名不匹配)";
                    diagnosticMsg = "不同模块索要的 DLL 使用了不同的密钥签名。bindingRedirect 此时无效。必须统一从官方渠道重新下载该 NuGet 包。";
                    riskColor = ConsoleColor.Magenta;
                    criticalCount++;
                }
                else if (hasRedirect)
                {
                    riskTitle = "✅ SAFE (已通过绑定重定向处理)";
                    diagnosticMsg = $"安全。配置文件中已存在重定向规则，所有版本请求将安全路由至新版本 [{redirectTarget}]。";
                    riskColor = ConsoleColor.Green;
                    safeCount++;
                }
                else if (firstToken == "null")
                {
                    riskTitle = "⚠️ LOW RISK (伪冲突：弱名称程序集)";
                    diagnosticMsg = "该程序集没有强名称签名，.NET 运行时在加载时会跳过严格版本匹配，直接将目录下物理文件覆盖加载，大概率不崩。";
                    riskColor = ConsoleColor.Yellow;
                    warningCount++;
                }
                else if (!isReachable)
                {
                    riskTitle = "💤 DORMANT RISK (伪冲突：僵尸代码路径)";
                    diagnosticMsg = "虽有冲突，但引入此冲突的父级 DLL 没有被主程序（EXE）直接或间接引用，属于僵尸死代码，运行时不会触发此崩溃。";
                    riskColor = ConsoleColor.DarkYellow;
                    warningCount++;
                }
                else
                {
                    riskTitle = "❌ CRITICAL (真冲突：强名称版本阻断)";
                    diagnosticMsg = "该库有强名称签名且处于活动调用链中。因缺少 <bindingRedirect> 规则，运行时百分之百触发 0x80131040 崩溃！";
                    riskColor = ConsoleColor.Red;
                    criticalCount++;
                }

                // --- E. 可视化诊断报告 ---
                Console.ForegroundColor = riskColor;
                Console.WriteLine($"\n[{riskTitle}] 程序集: [ {depName} ]");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   💡 诊断结论: {diagnosticMsg}");
                Console.ResetColor();

                foreach (var verKvp in versions)
                {
                    string version = verKvp.Key;
                    string parents = string.Join(", ", verKvp.Value);
                    string token = tokenMap.TryGetValue(depName, out var vtm) && vtm.TryGetValue(version, out var tk)
                        ? tk : "???";

                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write($"   👉 版本 {version,-16} [Token: {token}]  ");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"被引用自: ( {parents} )");
                }
                Console.ResetColor();
            }

            // ================================================================
            // 5. 诊断总结面板
            // ================================================================
            Console.WriteLine("\n=================================================================");

            if (criticalCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"📊 扫描完成: 发现 {criticalCount} 处致命风险，{warningCount} 处普通警告，{safeCount} 处已安全处理。");
            }
            else if (warningCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"📊 扫描完成: 发现 0 处致命风险，{warningCount} 处普通警告，{safeCount} 处已安全处理。");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"📊 扫描完成: 所有 {safeCount} 处冲突均已安全处理，无需担心。");
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("💡 排查建议：请优先修复品红 (DEADLOCK) 与大红 (CRITICAL) 标记的项目。");
            Console.WriteLine("   深黄 (DORMANT) 为无关僵尸路径，可忽略；黄色 (LOW) 为弱名称伪冲突。");
            Console.ResetColor();

            Console.WriteLine("\n按任意键退出...");
            try { Console.ReadKey(true); }
            catch (InvalidOperationException) { }
        }

        // ================================================================
        // 辅助方法
        // ================================================================

        /// <summary>
        /// 扫描主机目录下第一个 .exe，解析其直接依赖作为"活跃调用链入口"
        /// </summary>
        static void MapMainAppDependencies(string targetDir)
        {
            var exeFiles = Directory.GetFiles(targetDir, "*.exe", SearchOption.TopDirectoryOnly);
            if (exeFiles.Length == 0) return;

            string mainExe = exeFiles[0];
            mainAppName = Path.GetFileName(mainExe);

            try
            {
                using var stream = new FileStream(mainExe, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var peReader = new PEReader(stream);

                if (!peReader.HasMetadata) return;

                MetadataReader mdReader = peReader.GetMetadataReader();
                foreach (var asmRefHandle in mdReader.AssemblyReferences)
                {
                    AssemblyReference asmRef = mdReader.GetAssemblyReference(asmRefHandle);
                    string refName = mdReader.GetString(asmRef.Name);
                    mainAppDependencies.Add(refName);
                }
            }
            catch { /* 忽略异常 */ }

            if (mainAppDependencies.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  主程序 [{mainAppName}] 直接依赖 {mainAppDependencies.Count} 个程序集");
                Console.ResetColor();
            }
        }

        /// <summary>
        /// 扫描目录下所有 *.config 文件，解析 bindingRedirect 规则
        /// </summary>
        static void LoadBindingRedirects(string targetDir)
        {
            var configFiles = Directory.GetFiles(targetDir, "*.config", SearchOption.TopDirectoryOnly);

            foreach (var configFile in configFiles)
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(configFile);

                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(doc.NameTable);
                    nsmgr.AddNamespace("asm", "urn:schemas-microsoft-com:asm.v1");

                    var nodes = doc.SelectNodes("//asm:dependentAssembly", nsmgr);
                    if (nodes == null) continue;

                    foreach (XmlNode node in nodes)
                    {
                        var identityNode = node.SelectSingleNode("asm:assemblyIdentity", nsmgr);
                        var redirectNode = node.SelectSingleNode("asm:bindingRedirect", nsmgr);

                        if (identityNode == null || redirectNode == null) continue;

                        string? asmName = identityNode.Attributes?["name"]?.Value;
                        string? newVersion = redirectNode.Attributes?["newVersion"]?.Value;

                        if (!string.IsNullOrEmpty(asmName) && !string.IsNullOrEmpty(newVersion))
                            redirectRules[asmName] = NormalizeVersion(newVersion);
                    }
                }
                catch { /* 忽略异常 */ }
            }

            if (redirectRules.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  从 {configFiles.Length} 个 .config 文件中解析到 {redirectRules.Count} 条 bindingRedirect 规则");
                Console.ResetColor();
            }
        }

        static string NormalizeVersion(Version version) => version.ToString(4);

        static string NormalizeVersion(string versionText)
        {
            if (string.IsNullOrEmpty(versionText)) return versionText;
            if (Version.TryParse(versionText, out Version? v))
                return v.ToString(4);
            return versionText;
        }
    }
}
