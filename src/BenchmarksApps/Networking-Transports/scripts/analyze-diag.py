"""Summarizes the DIAG_* lines that ScalingDiagnostics / EpollDiagnostics print into a crank application log.

usage: analyze-diag.py <log> <rps> [<log> <rps> ...]
Prints, for the steady load window, CPU split, per-group cores, and per-request rates (using the wrk requests/second as the request rate).
"""
import re
import statistics
import sys


def kv(line):
    return {k: v for k, v in re.findall(r"(\w+)=(\S+)", line)}


def parse(path):
    cpu, thr, rt, ep, percpu = {}, {}, {}, {}, {}
    for raw in open(path, errors="replace"):
        i = raw.find("DIAG_")
        if i < 0:
            continue
        line = raw[i:].strip()
        name = line.split(" ", 1)[0]
        f = kv(line)
        if name == "DIAG_PERCPU":
            percpu[int(f["t"]) if "t" in f else 0] = [int(x) for x in line.split("t=")[1].split(" ")[1:]]
            continue
        if "t" not in f:
            continue
        t = int(f["t"])
        if name == "DIAG_CPU":
            cpu[t] = {k: float(v) for k, v in f.items() if k != "t"}
        elif name == "DIAG_THR":
            thr.setdefault(t, {})[f["group"]] = {k: float(v) for k, v in f.items() if k not in ("t", "group")}
        elif name == "DIAG_RT":
            rt[t] = {k: float(v) for k, v in f.items() if k != "t"}
    # DIAG_EPOLL has no t; number them in order of appearance (1/s).
    n = 0
    for raw in open(path, errors="replace"):
        if "DIAG_EPOLL" in raw:
            n += 1
            f = kv(raw[raw.find("DIAG_EPOLL"):])
            ep[n] = {k: float(v) for k, v in f.items()}
    return cpu, thr, rt, ep, percpu


def steady(cpu):
    # io_uring threads that wait in the kernel make idle cores show up as iowait, so iowait counts as idle.
    busy = {t: 100 - v["idle"] - v.get("iowait", 0) for t, v in cpu.items()}
    if not busy:
        return []
    peak = max(busy.values())
    ts = sorted(t for t, b in busy.items() if b >= 0.6 * peak)
    # Longest contiguous run, trimmed by one second on each side.
    runs, cur = [], []
    for t in ts:
        if cur and t != cur[-1] + 1:
            runs.append(cur)
            cur = []
        cur.append(t)
    runs.append(cur)
    best = max(runs, key=len)
    return best[1:-1] if len(best) > 4 else best


def main():
    args = sys.argv[1:]
    for path, rps in zip(args[0::2], args[1::2]):
        rps = float(rps)
        cpu, thr, rt, ep, _ = parse(path)
        window = steady(cpu)
        if not window:
            print(path, "no steady window")
            continue
        t0, t1 = window[0], window[-1]
        secs = len(window)
        print(f"== {path}  steady window t={t0}..{t1} ({secs}s)  wrk rps={rps:,.0f}")
        avg = lambda key: statistics.mean(cpu[t][key] for t in window)
        print("  cpuset: " + "  ".join(f"{k}={avg(k):.1f}%" for k in ("user", "sys", "irq", "softirq", "idle")))
        groups = {}
        for t in window:
            for g, v in thr.get(t, {}).items():
                a = groups.setdefault(g, {"userCores": [], "sysCores": [], "vcs": [], "ivcs": [], "waitCores": [], "threads": []})
                for k in a:
                    a[k].append(v[k])
        reqs = rps * secs
        total_cores = sum(statistics.mean(a["userCores"]) + statistics.mean(a["sysCores"]) for a in groups.values())
        print(f"  process cores {total_cores:.2f}  -> {total_cores / rps * 1e6:.2f} cpu-us/request")
        for g, a in sorted(groups.items(), key=lambda kv_: -(sum(kv_[1]["userCores"]) + sum(kv_[1]["sysCores"]))):
            u, s = statistics.mean(a["userCores"]), statistics.mean(a["sysCores"])
            if u + s < 0.05:
                continue
            print(f"    {g:<14} threads={statistics.mean(a['threads']):>5.0f}  user={u:6.2f}  sys={s:6.2f} cores   user/req={u / rps * 1e6:5.2f}us sys/req={s / rps * 1e6:5.2f}us   "
                  f"vcs/req={sum(a['vcs']) / reqs:5.2f} ivcs/req={sum(a['ivcs']) / reqs:5.2f} runqueue-wait={statistics.mean(a['waitCores']):.2f} cores")
        d = lambda key, table: (table[t1][key] - table[t0 - 1][key]) if (t0 - 1) in table else (table[t1][key] - table[t0][key])
        if rt:
            n = max(1, secs)
            print(f"  runtime: tpThreads={rt[t1]['tpThreads']:.0f} tpWorkItems/req={d('tpCompleted', rt) / reqs:.3f} lockContention/req={d('lockContention', rt) / reqs:.4f} "
                  f"gc0={d('gc0', rt):.0f} gc2={d('gc2', rt):.0f} alloc/req={d('allocBytes', rt) / reqs:.0f}B gcPause={d('gcPauseMs', rt):.0f}ms")
        if ep:
            # DIAG_EPOLL lines are printed once per second from the first worker registration; align by counting from the end.
            keys = sorted(ep)
            last = ep[keys[-1]]
            # find the sample pair whose recv delta window matches secs by taking the busiest `secs` consecutive deltas
            deltas = [(ep[k]["recv"] - ep[k - 1]["recv"], k) for k in keys[1:]]
            best = max(range(len(deltas) - secs + 1), key=lambda i: sum(x[0] for x in deltas[i:i + secs]))
            a, b = keys[1:][best] - 1, keys[1:][best + secs - 1]
            r = lambda key: (ep[b][key] - ep[a][key])
            recv = r("recv")
            per = lambda key: r(key) / recv if recv else 0
            print(f"  epoll (per recv): epoll_wait={per('waits'):.2f} events={per('events'):.2f} send={per('send'):.2f} wakeWrite={per('wakeWrites'):.2f} wakeRead={per('wakeReads'):.2f} "
                  f"commands={per('commands'):.2f} epoll_ctl={per('controls'):.2f} recvEAGAIN={per('recvBlocked'):.2f} loopIterations={per('iterations'):.2f}  conns min/max={last['minConns']:.0f}/{last['maxConns']:.0f}")


main()
