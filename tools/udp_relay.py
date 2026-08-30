"""A minimal UDP relay, used to prove the game plays through a tunnel service.

Tunnel providers (playit.gg and friends) do exactly this: they accept UDP on a
public address and forward it to the host's private one, so the host sees every
packet arriving from the tunnel rather than from the real peer. That address
rewriting is the part that can quietly break a UDP protocol, so it is worth
testing against rather than assuming.

    python tools/udp_relay.py 25999 127.0.0.1 24555 [--delay MS] [--loss PCT]

Host the match on 24555 as usual, have the guest join "127.0.0.1:25999", and the
traffic takes the same shape it would over a real tunnel. --delay and --loss add
the latency and packet loss a loopback test would otherwise flatter away.
"""

import random
import select
import socket
import sys
import time


def main() -> int:
    argv = sys.argv[1:]
    delay_ms = 0.0
    loss = 0.0
    for flag, setter in (("--delay", "delay"), ("--loss", "loss")):
        if flag in argv:
            i = argv.index(flag)
            value = float(argv[i + 1])
            if setter == "delay":
                delay_ms = value
            else:
                loss = value / 100.0
            del argv[i:i + 2]

    if len(argv) != 3:
        print(__doc__)
        return 2

    listen_port = int(argv[0])
    upstream = (argv[1], int(argv[2]))
    if delay_ms or loss:
        print(f"[relay] simulating {delay_ms:.0f}ms each way, {loss * 100:.1f}% loss", flush=True)

    front = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    front.bind(("0.0.0.0", listen_port))
    print(f"[relay] :{listen_port} <-> {upstream[0]}:{upstream[1]}", flush=True)

    # One upstream socket per client, so replies can be routed back to the right
    # peer -- the same per-flow mapping a NAT keeps.
    to_upstream: dict[tuple[str, int], socket.socket] = {}
    to_client: dict[socket.socket, tuple[str, int]] = {}
    last_seen: dict[socket.socket, float] = {}
    packets = 0
    dropped = 0
    # (due_at, socket, payload, destination) held back to emulate link latency.
    queue: list[tuple[float, socket.socket, bytes, tuple[str, int]]] = []

    while True:
        timeout = 1.0
        if queue:
            timeout = max(0.0, min(q[0] for q in queue) - time.monotonic())
        readable, _, _ = select.select([front] + list(to_client), [], [], timeout)
        now = time.monotonic()

        due = [q for q in queue if q[0] <= now]
        queue = [q for q in queue if q[0] > now]
        for _, sock, payload, dest in due:
            try:
                sock.sendto(payload, dest)
            except OSError:
                pass

        for sock in readable:
            try:
                data, addr = sock.recvfrom(65535)
            except OSError:
                continue

            if sock is front:
                peer = to_upstream.get(addr)
                if peer is None:
                    peer = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                    peer.bind(("0.0.0.0", 0))
                    to_upstream[addr] = peer
                    to_client[peer] = addr
                    print(f"[relay] new client {addr[0]}:{addr[1]}", flush=True)
                out_sock, dest = peer, upstream
                last_seen[peer] = now
            else:
                client = to_client.get(sock)
                if client is None:
                    continue
                out_sock, dest = front, client
                last_seen[sock] = now

            packets += 1
            if loss and random.random() < loss:
                dropped += 1
            elif delay_ms:
                queue.append((now + delay_ms / 1000.0, out_sock, data, dest))
            else:
                out_sock.sendto(data, dest)

            if packets % 200 == 0:
                print(f"[relay] {packets} forwarded, {dropped} dropped", flush=True)

        # Drop idle flows the way a NAT expires its table.
        for peer in [p for p, t in last_seen.items() if now - t > 120.0]:
            client = to_client.pop(peer, None)
            to_upstream.pop(client, None)
            last_seen.pop(peer, None)
            peer.close()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        pass
