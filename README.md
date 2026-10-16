# RunBasedPassives

局内被动属性成长，通过行动累积升级，永久保存。

版本：0.4.1

## 致谢

本模组 UI 基于 **王子晨 (WZC_UI)** 的 MoreUI 源码抽离出的 MoreUIPanel 库。

## 概述

在关卡中通过跳跃、抓取物品、移动、受到治疗、蹲伏恢复累积经验，达到阈值后永久提升对应属性。所有成长跨局保留，属性数值由模组直接应用，升级后立即生效。同一存档内换层时未填满的进度会自动继承，切换存档则重新开始。

## ⚠️ 重要说明

- **联机同步**：各自独立累积，互不影响。
- **软依赖**：需安装 MoreUIPanel 库模组才能显示信息面板。

## 🔧 冲突模组

以下模组与 RunBasedPassives 存在已知冲突：

- **SharedUpgrades / SharedUpgrades Patch / SharedUpgradesChance**
- **SharedUpgradesPlus**
- **BetterTeamUpgrades**
- **Empress SharedUpgrades**
- **LateJoinSharedUpgradesByNastyPablo**
- **hairo-SharedUpgrades**

这些模组会同步升级字典到所有玩家，与我们的被动等级存储方式冲突。**不要同时使用。**

- **InfiniteStamina / MoreStamina 等体力修改模组**
- **InfiniteGrab 等抓取力量修改模组**
- **MoreUpgrades（开启"允许团队升级"或"同步主机升级"时）**

## ✨ 功能

- **跳跃升级**：每跳跃一定次数，额外跳跃次数 +1
- **力量升级**：每抓取一定数量不同物品，抓取力量 +1
- **耐力升级**：每移动一定距离，体力上限 +10
- **生命升级**：每受到一定次数有效治疗，最大生命 +20
- **冲刺速度升级**：冲刺时累积距离，每升一级冲刺速度 +1.0（与原版升级幅度一致），可配置封顶等级
- **蹲伏恢复升级**：蹲伏时恢复体力，基于实际恢复量累积经验，恢复速度每级 +1
- **进度继承**：同一存档内换层时，未完成的升级进度会保留到下一关，不再从零开始
- **存档隔离**：切换存档后经验自动清零，不同存档独立计算
- **动态递增阈值**：升级门槛随等级提高，避免后期属性爆炸
- **属性实时应用**：所有属性每帧强制应用，升级瞬间生效
- **属性永久保存**：等级存储于游戏原版升级字典，跟随存档，与商店升级完美兼容
- **左下角实时进度显示**：通过 MoreUIPanel 库显示当前经验与等级
- **六个属性可单独开关**
- **冲刺速度封顶可配置**：可在配置文件中设置封顶等级，0 为不封顶
- **轻量、无额外功能**

## 📋 未来规划

以下功能正在考虑中。

- 更多被动属性：抓取范围、翻滚距离等
- 属性 API：供其他模组注册自定义被动属性，实现统一管理
## 安装

- 确保已安装 BepInExPack 和 MoreUIPanel
- 将 `RunBasedPassives.dll` 放入 `BepInEx/plugins` 文件夹
- 启动游戏，模组会自动生成配置文件

## ⚙️ 配置

配置文件位置：`BepInEx/config/cn_xc.RunBasedPassives.cfg`

| 配置项 | 默认值 | 说明 |
|---|---|---|
| EnableJump | true | 启用跳跃升级 |
| EnableStrength | true | 启用力量升级 |
| EnableStamina | true | 启用耐力升级 |
| EnableHealth | true | 启用生命升级 |
| EnableSprintSpeed | true | 启用冲刺速度升级 |
| EnableCrouchRestore | true | 启用蹲伏恢复升级 |
| SprintSpeedCap | 0 | 冲刺速度封顶等级（0=不封顶） |
| VerboseLogging | false | 详细日志（调试用） |

## ❓ 常见问题

**Q：冲刺速度太快怎么办？**  
A：可在配置文件中设置 `SprintSpeedCap` 为一个合适的等级，超过该等级后速度不再增长。

**Q：换存档后属性会继承吗？**  
A：属性跟随游戏存档，每个存档独立累积。切换存档后属性恢复为对应存档的进度。

**Q：如何查看当前等级？**  
A：屏幕左下角会实时显示等级和进度。需要 MoreUIPanel 依赖。

**Q：能否手动调整阈值？**  
A：暂不支持，可通过修改源码中的常数自行编译。


---

## 许可证

本项目以 **GNU General Public License v3.0** 发布。

Copyright (C) 2026 cn_xc

完整条款见仓库根目录 [LICENSE](LICENSE)。
