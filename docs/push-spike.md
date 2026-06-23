# Spike: zero-install via device server-push

**Question:** can devices push to a central server so schools need **no software**?
**Verdict:** **Yes, the SDK fully supports it.** It ships a purpose-built server
component (`RealSvrOcxTcp`) that listens for devices pushing real-time punches.
Below is what it exposes and the trade-offs of using it.

## What the server component gives us

`RealSvrOcxTcp` (32-bit COM control, `Interop.RealSvrOcxTcpLib.dll`):

```
int  OpenNetwork(int port)                    // start listening (one port = all devices)
int  CloseNetwork(int port)
int  SendRtLogResponseV1(ip, port, response)  // ACK a punch back to the device
int  SendRtLogResponseV3(ip, port, response)  // (devices buffer + retry until ACKed)

event OnReceiveGLogData(
        string deviceIP, int devicePort, int deviceID,
        int enrollNumber, int verifyMode, int inOutMode,
        DateTime logDate, string serialNo)     // <-- a punch, in real time
event OnReceiveGLogDataExtend(string rootIP, ...same..., serialNo)
event OnReceiveGLogText / ...AndImage / ...OnDoorOpen   // text/face-image variants
```

The punch event carries the device **serial number** — that's the key that lets a
**multi-tenant** server route each punch to the right school, regardless of NAT.
The `SendRtLogResponse*` ACK means devices use **store-and-forward** (buffer and
retry until acknowledged), so brief outages don't lose punches.

## Architecture (no agent at any school)

```
Each device (set once at mount: Server IP/Port + "ServerRequest" on)
        │  pushes punches (real time, proprietary TCP)
        ▼
Central listener service (Windows, x86)  — hosts RealSvrOcxTcp, OpenNetwork(port)
        │  OnReceiveGLogData(serialNo, enroll, verify, inout, date)
        │  serialNo -> tenant/site/device (registry)  ->  insert bio_punch  ->  ACK
        ▼
Shikzya shows attendance (same DB/UI as today)
```

On-site work shrinks to a **one-time device setting** (server IP/port), done by the
installer who is already there mounting the device. Nothing runs on a school PC.

## What it would take to build

1. A small **central Windows x86 service** that instantiates the OCX on an STA
   thread **with a message pump** (ActiveX controls need one even headless — the
   main engineering risk), calls `OpenNetwork`, handles `OnReceiveGLogData`,
   decodes verify/in-out with our existing `FkLogDecoder`, writes `bio_punch`, and
   ACKs via `SendRtLogResponseV3`.
2. Add **serial number** to the device registry so `serialNo -> tenant/site`.
3. A **public TCP endpoint** for the listener that schools can reach outbound.
4. Set each device's **server IP/port** at install (device keypad or via the SDK).

## Honest trade-offs vs the agent

| | Agent (current) | Central push |
|---|---|---|
| School-side software | one small app/service | **none** |
| Transport | outbound HTTPS | raw proprietary **TCP**, inbound to a public port |
| Security | TLS, no inbound | no TLS; needs IP allowlist / VPN to harden |
| Reliability | pull = bulletproof reconciliation | store-and-forward ACK (good); long outages bounded by device buffer |
| Hosting risk | low | **32-bit ActiveX in a service + message pump** (fiddly) |
| Device config | none (all central) | one-time server IP/port per device at mount |

## Recommendation

It's viable and genuinely removes all school-side software. Before betting on it,
build a **small proof-of-concept**: host the OCX, `OpenNetwork`, point the office
Bio-27 at it (set its server IP/port), and confirm a punch arrives via
`OnReceiveGLogData` with the serial number and that the ACK sticks. If the PoC is
clean, promote it to a hardened central service; if the ActiveX-in-a-service or the
public-TCP exposure prove painful, keep the agent (now a double-click install) as
the default and use push only where it's wanted.
