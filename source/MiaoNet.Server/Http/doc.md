# MiaoNet 内部服务端 HTTP API

服务默认监听 `http://localhost:21474/`，可通过 `MiaoServer.HttpListenerPrefix` 修改。接口没有内置认证，只应暴露在受信网络或受保护的反向代理之后。未知路径返回 `404 Not Found`。

## `GET /status`

返回在线人数、频道及各频道玩家：

```json
{
  "PlayersCount": 2,
  "Channels": [
    {
      "ID": 0,
      "Name": "main",
      "Players": [
        {
          "ID": 5,
          "Name": "wheat",
          "Location": "Celeste/LostLevels A intro-00-past"
        }
      ]
    }
  ]
}
```

成功返回 `200 OK` 和 JSON。

## `DELETE /player`

踢出单个连接或同一认证 ID 下的所有连接。

| 参数 | 必需 | 说明 |
|---|---|---|
| `reason` | 是 | 返回给客户端的踢出原因 |
| `cid` | 与 `aid` 二选一 | 精确连接 ID |
| `aid` | 与 `cid` 二选一 | 认证 ID，匹配其所有连接 |

`cid` 优先于 `aid`。成功返回 `204 No Content`；缺少/错误参数返回 `400 Bad Request`；`cid` 不在线返回 `404 Not Found`；其他方法返回 `405 Method Not Allowed`。使用 `aid` 时即使没有匹配连接也返回 `204`。

## `/announce?msg=...`

广播服务端聊天消息。`msg` 不能为空或纯空白。handler 不限制 HTTP 方法；成功返回 `204 No Content`，无效消息返回 `400 Bad Request`。

## `/gc`

强制执行压缩的 Full GC 并等待 finalizer。handler 不限制 HTTP 方法，成功返回 `204 No Content`。这是有明显运行时影响的管理操作。

## `GET /metrics`

返回在线人数、累计网络指标和 GC 数据：

```json
{
  "OnlinePlayersCount": 2,
  "Metrics": {
    "TcpUploadByBytes": 821980,
    "TcpDownloadByBytes": 1658598,
    "TcpUploadByPackets": 25844,
    "TcpDownloadByPackets": 35463,
    "SessionsCount": 6
  },
  "GC": {
    "TotalAllocatedBytes": 66472352,
    "TotalMemory": 8118432,
    "TotalPauseDuration": "00:00:00.0165670"
  }
}
```

当前 handler 不限制 HTTP 方法；成功返回 `200 OK` 和 JSON。
