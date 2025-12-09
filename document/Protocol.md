# Handshake

- 16 bytes: Handshake 头
- Handshake Packet (无 'type' 字段)

# Packet

- `uint16`: size
- `uint16`: type
- data

# Primitive

## List\<T\>

- `int32`: 7-bit encoded count
- items

## String

- `uint16`: length
- UTF8 encoded data

## Version

- `uint16`: Major
- `uint16`: Minor
- `uint16`: Patch
