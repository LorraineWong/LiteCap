# LiteCap 中文说明

[English](README.md)

截图永远是 JPG、永远小于 100 KB，并以**文件形式**复制到剪贴板。自带标注和离线文字识别。支持 Windows 和 macOS。

界面默认英文，可在 **Settings → General → Language** 切换为中文。

## 功能

- **圈选 / 全屏**：拖动圈选或单击选窗口；全屏直接保存
- **永远小于上限**：默认 100 KB（可调），自动调质量，实在不够才缩小尺寸
- **以文件复制**：直接粘贴到邮件、聊天、上传框
- **标注**：矩形、椭圆、箭头、画笔、荧光笔、文字、马赛克
- **离线识别文字**：用系统自带引擎，截图不离开电脑
- **延时 3 秒圈选**：用来截右键菜单、悬停提示

## 安装

**Windows 10 / 11**
1. 在 [Releases](https://github.com/LorraineWong/LiteCap/releases/latest) 下载 `LiteCap-1.0.0-windows.zip` 并解压
2. 运行 `LiteCap.exe`，图标出现在右下角托盘
3. 出现 "Windows protected your PC" 时：More info → Run anyway

**macOS 13 及以上**
1. 下载 `LiteCap-1.0.0-macos.zip` 并解压
2. 打开「终端」，输入 `bash `（后面有空格），把 `Install.command` 拖进去，按回车
3. 系统设置 → 隐私与安全性 → 屏幕录制：打开 LiteCap，然后退出并重新打开 LiteCap

> 每次重新编译后，要把屏幕录制里 LiteCap 的开关关掉再打开。截图全黑就是这个原因。

## 快捷键

| 操作 | Windows | macOS |
|---|---|---|
| 圈选截图 | `Ctrl+1` | `⇧⌘1` |
| 全屏截图 | `Ctrl+2` | `⇧⌘2` |
| 识别文字 | 托盘菜单 / 编辑时按 `O` / 可自设快捷键 | 菜单栏 / 编辑时按 `O` / 可自设快捷键 |

编辑工具：`R` 矩形 · `E` 椭圆 · `A` 箭头 · `P` 画笔 · `H` 荧光笔 · `T` 文字 · `M` 马赛克 · `1/2/3` 粗细 · 撤销 `Ctrl/⌘+Z`
完成：`Enter` / 空格 / `Ctrl/⌘+C` / 双击选区；取消：`Esc`

## Windows 提示"没有可用的识别语言"

设置 → 时间和语言 → 语言和区域 → 中文（简体）或 English → ⋯ → 语言选项 → 安装「光学字符识别」。
