# SharpZipLib 0.86 许可与链接例外

- 组件：`Libraries/ICSharpCode.SharpZipLib.dll`
- 程序集版本：`0.86.0.518`（#ziplib 0.86.0，构建于 2010-05-25）
- 版权：`Copyright 2001-2010 Mike Krueger, John Reilly`
- 许可：GNU GPL v2，附加 GNU Classpath 风格链接例外；全文见 [GPL-2.0.txt](GPL-2.0.txt)
- 来源与下载页：<http://icsharpcode.github.io/SharpZipLib/legacy.html>
- 对应源码包：<https://sourceforge.net/projects/sharpdevelop/files/SharpZipLib/0.86/SharpZipLib_0860_SourceSamples.zip/download>

## 链接例外原文

> Linking this library statically or dynamically with other modules is making a combined work based on this library. Thus, the terms and conditions of the GNU General Public License cover the whole combination.
>
> As a special exception, the copyright holders of this library give you permission to link this library with independent modules to produce an executable, regardless of the license terms of those independent modules, and to copy and distribute the resulting executable under terms of your choice, provided that you also meet, for each linked independent module, the terms and conditions of the license of that module. An independent module is a module which is not derived from or based on this library. If you modify this library, you may extend this exception to your version of the library, but you are not obligated to do so. If you do not wish to do so, delete this exception statement from your version.

## 对本项目的含义

1. UPandaFramework 自身的 MIT 代码可以作为“独立模块”与 SharpZipLib 链接，不必改为 GPL；闭源商业分发是允许的。
2. 分发随包的 SharpZipLib DLL 时，必须同时提供 GPLv2 全文（已随本目录）与上述例外声明。
3. GPLv2 第 3 节要求：分发目标代码时，要么随附库的完整源码，要么提供至少三年有效的书面源码获取承诺。当前仓库未随附源码，因此发布说明里必须给出源码获取方式（上面的 SourceForge 源码包即为对应版本）。
4. 直接使用方是 `Excel.dll`（ExcelDataReader 2.1.2）内部的 `ICSharpCode.SharpZipLib.Zip`，框架源码没有直接引用；但运行时打开 `.xlsx` 会走到该 DLL，不能简单删除。
