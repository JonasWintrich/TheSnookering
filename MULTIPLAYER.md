# Playing Snookering against a friend

Two players, one hosts. The game sends only a tiny shot description over the
network and both machines simulate the shot identically, so it stays in step even
on a poor connection — there is no ball-position streaming to stutter.

## Same house / same WiFi

1. **Host:** Main menu → **Play Online** → **Host a match**. The screen shows your
   local address, e.g. `192.168.1.42`.
2. **Guest:** Main menu → **Play Online** → type that address → **Join**.
3. **Host:** pick 8-Ball or Snooker → **Start match**.

## Over the internet

Your machines cannot reach each other directly by default. Two options:

### Tailscale (recommended — no router configuration)

Free, takes about five minutes, and works even on connections where port
forwarding is impossible (most mobile and many fibre ISPs use carrier-grade NAT).

1. Both players install [Tailscale](https://tailscale.com/download) and sign in
   with the same account, or one invites the other to their tailnet.
2. The host runs `tailscale ip -4` (or reads it in the Tailscale app) to get an
   address like `100.87.4.12`.
3. Host the match as above; the guest joins using that Tailscale address.

Everything then behaves exactly like a LAN game.

### Port forwarding (if you control the router)

1. Host forwards **UDP port 24555** to their computer's local IP.
2. Host finds their public IP (e.g. from whatismyip.com).
3. Guest joins using that public IP.

This fails if your ISP does not give you a real public IP — if in doubt, use
Tailscale instead.

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
