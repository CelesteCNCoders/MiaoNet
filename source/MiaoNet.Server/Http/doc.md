# MiaoNet Internal Server HTTP API

终结点未找到返回 `404 Not Found`

## `/player`

### `DELETE`

将指定 `cid` 连接 id 的玩家或所有 `aid` 对应的玩家踢出.
踢出成功后会向全服广播一条服务器消息(按 `cid` 踢出显示"已被踢出服务器", 按 `aid` 踢出显示"被封禁", 均附带 `reason`).

| 参数名 | 类型 | 必须 | 说明 |
| :--- | :--- | :--- | :--- |
| `reason` | string | 是 | 踢出原因 |
| `cid` | int | 否 | 连接 ID (Connection ID), 精确匹配单个连接 |
| `aid` | int | 否 | 认证 ID (Auth ID, 即论坛侧 ID), 匹配该 ID 下的所有连接 |

无返回内容.

响应码:
- `204 No Content`
- `400 Bad Request`: 参数不完整或格式错误(未提供 `reason` 或 ID)
- `404 Not Found`: 指定的 `cid` 不在线
- `405 Method Not Allowed`: 未使用 `DELETE`

---

## `/status`

获取在线概况及各频道内的玩家分布。

返回示例:
```json
{
  "PlayersCount": 2, // 当前玩家数
  "Channels": [ // 所有频道状态
    {
      "ID": 0, // 频道 id
      "Name": "main", // 频道名称
      "Players": [ // 该频道玩家列表
        {
          "ID": 5, // 玩家连接 ID
          "Name": "3H8ZX6qP", // 玩家名称
          "Location": "Celeste/LostLevels A intro-00-past" // 玩家所在位置(不透明)
        },
        {
          "ID": 6,
          "Name": "sMs2qfhk",
          "Location": "Celeste/LostLevels A intro-00-past"
        }
      ]
    }
  ]
}
```

响应码:
- `200 OK`

---

## `/metrics`

获取指标信息

返回示例:
```json
{
  "OnlinePlayersCount": 2, // 在线玩家数
  "Metrics": {
    "TcpUploadByBytes": 821980, // TCP 总上行字节数
    "TcpDownloadByBytes": 1658598, // TCP 总下行字节数
    "TcpUploadByPackets": 25844, // TCP 总上行应用层包数
    "TcpDownloadByPackets": 35463, // TCP 总下行应用层包数
    "SessionsCount": 6 // 历史总连接建立成功数
  },
  "GC": {
    "TotalAllocatedBytes": 66472352, // GC 总 Alloc 字节数
    "TotalMemory": 8118432, // GC 堆去除碎片内存大小
    "TotalPauseDuration": "00:00:00.0165670" // GC 总 STW 时长
  }
}
```

响应码:
- `200 OK`