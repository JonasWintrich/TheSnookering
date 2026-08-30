# Playing Snookering against a friend

Two players, one hosts. The game sends only a tiny shot description over the
network and both machines simulate the shot identically, so it stays in step even
on a poor connection — there is no ball-position streaming to stutter.

## Same house / same WiFi

1. **Host:** Main menu → **Play Online** → **Host a match**. The screen shows your
   local address, e.g. `192.168.1.42`.
2. **Guest:** Main menu → **Play Online** → type that address → **Join**.
3. **Host:** pick 8-Ball or Snooker → **Start match**.

## Different networks (over the internet)

Two computers behind two home routers cannot reach each other directly — neither
has an address the other can dial. This is how the internet works rather than a
limitation of the game, and every online game solves it the same way: something
publicly reachable sits in the middle. You only need to set that up **on the host
side**; the guest just types an address.

### A free tunnel (recommended — only the host sets anything up)

A tunnel service gives your computer a public address and forwards traffic to it,
without touching your router. It works behind carrier-grade NAT, where port
forwarding is impossible.

**Host, once:**

1. Download the agent from [playit.gg](https://playit.gg/download) and run it. It
   opens a browser so you can claim it with a free account.
2. In the playit dashboard: **Tunnels → Add Tunnel → UDP**, local port **24555**.
3. It shows you a public address like `147.185.221.23:12345` or
   `something.gl.at.ply.gg:12345`. That is what your friend needs.
4. Leave the agent running while you play.

**Then:** host the match in-game as usual, send your friend that address, and they
paste it — port and all — into the Join field. They install nothing.

> The Join field accepts a plain address (`1.2.3.4`), an address with a port
> (`1.2.3.4:12345`) and a hostname (`foo.gl.at.ply.gg:12345`). A tunnel hands out
> a port of its own choosing, so paste the whole thing.

### Tailscale (if you would rather both install something)

Free, about five minutes each. Both players install
[Tailscale](https://tailscale.com/download) and sign in to the same account (or
one invites the other). The host reads their address from the app — something
like `100.87.4.12` — and the guest joins with it. It then behaves exactly like a
LAN game.

### Port forwarding (only if you control the router)

Forward **UDP port 24555** to the host's local IP, then the guest joins using the
host's public IP. This silently fails on many mobile and fibre connections, which
do not hand out a real public IP — if in doubt, use a tunnel instead.

### Will it play well over a long connection?

Yes. The game sends one small shot description and both machines simulate it, so
latency delays when a shot *starts*, never how it plays out — no rubber-banding,
no stutter. It has been tested through a relay at 180 ms round trip with 4% packet
loss and stayed perfectly in sync; lost packets are simply re-sent.

## During the match

- You always see which side you are: the player cards read **You** and **Opponent**.
- You watch your opponent line up in real time — their cue moves, their spin dial
  and power meter update — then the shot plays out on both screens.
- While it is their turn your aiming input is disabled, but the camera is not:
  orbit and zoom freely to watch from any angle.
- The scoreboard, fouls, ball-in-hand and frame end are computed independently on
  both machines from the same rules engine, so they always agree.
- **Esc → Main menu** leaves the match; the other player is told the match ended.

## If something looks wrong

Both machines cross-check a fingerprint of the table and the score after every
shot. If they ever disagree, the host's version wins and the guest is corrected
automatically — you may see a brief "Resynced with the host" message. Tell me if
that happens often, as it would point to a real problem rather than a hiccup.

**Both players must run the same build.** A mismatched version is refused at
connect time with a clear message rather than being allowed to drift apart.
