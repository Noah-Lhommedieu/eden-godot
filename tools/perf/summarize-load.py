#!/usr/bin/env python3
"""Résumé chiffré d'un run de charge (PLAN-ACTION-3 lot N) : lit le dossier de sortie de eden_server/tools/load-test.sh.
Usage : tools/perf/summarize-load.py /tmp/load-50 [--md]   (percentiles estimés depuis les histogrammes Prometheus de la zone)"""
import json, re, sys, os

def hist(text, name):
    buckets, total, ssum = [], 0, 0.0
    for m in re.finditer(r'^%s_bucket\{[^}]*le="([^"]+)"[^}]*\} (\S+)' % re.escape(name), text, re.M):
        le = float('inf') if m.group(1) == '+Inf' else float(m.group(1)); buckets.append((le, float(m.group(2))))
    m = re.search(r'^%s_count(?:\{[^}]*\})? (\S+)' % re.escape(name), text, re.M); total = float(m.group(1)) if m else 0
    m = re.search(r'^%s_sum(?:\{[^}]*\})? (\S+)' % re.escape(name), text, re.M); ssum = float(m.group(1)) if m else 0
    return sorted(buckets), total, ssum

def pct(buckets, total, q):
    if total <= 0: return None
    target, prev_le, prev_c = q * total, 0.0, 0.0
    for le, c in buckets:
        if c >= target:
            if le == float('inf'): return prev_le
            return prev_le + (le - prev_le) * ((target - prev_c) / max(c - prev_c, 1e-9))
        prev_le, prev_c = le, c
    return prev_le

def main():
    d = sys.argv[1]; bots = json.load(open(os.path.join(d, 'bots.json')))
    text = open(os.path.join(d, 'zone-metrics-last.txt'), encoding='utf-8').read()
    out = {"sessions": bots["actualSessions"], "entered": bots["entered"], "errors": bots["errors"], "entry_ms_avg": round(bots["entryMs"]["avg"]), "entry_ms_max": round(bots["entryMs"]["max"]),
           "moves": bots["totals"]["moves"], "received_msgs": bots["totals"]["received"]}
    broken = sum(1 for s in bots.get("sessions", []) if s.get("Error"))
    out["sessions_with_error"] = broken
    for label, metric in [("tick", "eden_zone_tick_ms_milliseconds"), ("cmd_delay", "eden_zone_command_delay_ms_milliseconds"),
                          ("pkt_handle", "eden_packet_handle_ms_milliseconds"), ("pkt_dispatch", "eden_packet_dispatch_delay_ms_milliseconds")]:
        b, t, s = hist(text, metric)
        out[label] = {q: (None if (v := pct(b, t, p)) is None else round(v, 2)) for q, p in (("p50", .5), ("p95", .95), ("p99", .99))} | {"n": int(t)}
    gc = re.search(r'^dotnet_gc_pause_time_seconds_total\S* (\S+)', text, re.M); out["gc_pause_s_total"] = round(float(gc.group(1)), 3) if gc else None
    g2 = re.search(r'gc_heap_generation="gen2"\} (\S+)', text); out["gen2_collections"] = int(float(g2.group(1))) if g2 else None
    bo = re.search(r'^eden_bytes_out_total\S* (\S+)', text, re.M)
    if bo: out["out_KBps_per_session"] = round(float(bo.group(1)) / 1024 / max(bots["durationSeconds"], 1) / max(bots["actualSessions"], 1), 1)
    proc = [l.split() for l in open(os.path.join(d, 'zone-proc.txt')) if l.strip()]
    if proc:
        cpu = [float(x[1]) for x in proc]; rss = [int(x[2]) for x in proc]
        out["zone_cpu_pct_avg"] = round(sum(cpu) / len(cpu), 1); out["zone_cpu_pct_max"] = max(cpu); out["zone_rss_MB_max"] = round(max(rss) / 1024)
    pg = os.path.join(d, 'pgconn-max.txt')
    if os.path.exists(pg): out["pg_connections_max"] = int(re.search(r'(\d+)\s*$', open(pg).read().strip()).group(1))
    print(json.dumps(out, ensure_ascii=False, indent=1))

main()
