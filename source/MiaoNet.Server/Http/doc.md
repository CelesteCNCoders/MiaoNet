# MiaoNet 服务端 HTTP 接口

服务端开放了额外的 `Http` 接口, 监听地址读取自配置 `MiaoServer:Http:ListenerPrefix`(默认 `http://localhost:21474/`). 没有任何认证, 建议仅暴露在受信任的网络或进行反向代理等. 未注册的端口会返回 `404 Not Found`. `Debug` 构建下返回的 `JSON` 将会缩进.  
不过需要注意的, 就如项目一样, 这些接口也不稳定, 不保证任何形式的兼容性.

## `/status`

返回在线连接数以及各频道的情况, 不限制请求方法. 成功时返回 `200 OK` 以及这样的 JSON:

```json
{
  "PlayersCount": 2,
  "Channels": [
    {
      "ID": 0,
      "Name": "main",
      "IsPrivate": false,
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

`Location` 的内容不透明, 不建议尝试解析它.

## `/player`

暂时只接受 `DELETE`, 用于踢出玩家, 其它方法返回 `405 Method Not Allowed`. 查询参数:

- `reason`: 必需, 会作为原因发送给客户端
- `cid`: 连接 ID, 只踢出这一个连接
- `aid`: 认证 ID(论坛账号 ID), 踢出该 ID 下的所有连接

`cid` 与 `aid` 需要有其一, 参数异常时返回 `400 Bad Request`, 指定的 `cid` 不在线返回 `404 Not Found`, 其余情况返回 `204 No Content`.

## `/announce`

广播一条服务端聊天消息, 不限制请求方法. 查询参数 `msg` 为空或者纯空白返回 `400 Bad Request`, 否则返回 `204 No Content`.

## `/gc`

立即执行一次阻塞的压缩 Full GC 并等待 finalizer 结束, 不限制请求方法, 总是返回 `204 No Content`.

## `/metrics`

返回在线连接数, 累计的网络指标以及 GC 数据等, 不限制请求方法. 成功时返回 `200 OK` 以及如下 `JSON`:

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
