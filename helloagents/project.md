# 项目技术约定

## 技术栈
C# 14；客户端为 net8.0 的 Everest Mod；服务端与测试为 net10.0。共享协议、ClientShared 和 ChatInputBox 通过源码链接复用。

## 开发约定
4 空格、Allman 花括号、Nullable；协议 ID 由注册列表顺序决定，新类型只能追加。详见 [编码规范](../docs/coding-style.md)。
知识库基于当前代码与已有模块文档整理，运行行为以代码为准。

## 错误与日志
客户端使用 Everest Logger；服务端使用 Microsoft.Extensions.Logging。非法网络数据必须验证，不能信任客户端自报成绩。

## 测试与流程
使用 MSTest。涉及共享代码时构建实际消费者。客户端构建使用 CelesteRootPath 指定本机游戏，并可用 ModAssetsCopyType=none 避免安装。
本机测试框架需要回环套接字权限。提交、发布和启动服务不是默认构建步骤。
