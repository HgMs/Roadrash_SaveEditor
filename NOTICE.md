# NOTICE

## 代码许可

本仓库的**源代码**（`src/Launcher.cs`、`src/build_assets.py`、`src/ui/template.html`、脚本等）
以 [MIT License](LICENSE) 开源。

## 游戏素材

本仓库**不包含、也不分发**任何 Road Rash 的游戏资源。

界面上用到的图片（机车特写、赛道明信片、展厅背景）在**构建时**由
`src/build_assets.py` 从使用者本机的游戏安装目录读取：

```
<游戏目录>/Images/Bikes/{Rat,Sport,Super}/*.rri
<游戏目录>/Images/Postcard/*.rri
<游戏目录>/Images/Showroom/Super/Bikes.rri
```

这些图片的版权属于其权利人（Electronic Arts / 各原始作者）。
构建生成的 `src/ui/rrs_editor.html`（以及内嵌了图片的 exe）因此被 `.gitignore` 排除，
**不会进入本仓库，也不应作为公开二进制分发**。

如果你 fork 后要发布 Release，请发布**不带素材的占位版**（`build.ps1 -NoAssets`），
让用户在自己的机器上构建完整版；或者在 Release 中明确说明图片来自用户本机游戏。

## 截图

`docs/images/` 里的截图是作者在本机安装了游戏的环境下截的，用于展示界面效果；
它们不属于可再分发的素材，请勿单独转载其中的游戏画面。

## 商标

"Road Rash"、"暴力摩托" 及相关名称、标识的权利归其权利人所有。
本项目为非官方的个人工具，与 Electronic Arts 无任何关联，也未获其授权或背书。
