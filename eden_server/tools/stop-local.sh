#!/bin/bash
for pid in $(pgrep -x dotnet); do
  grep -qa "Eden\.\(Login\|World\|Zone\)\.dll" /proc/$pid/cmdline 2>/dev/null && kill "$pid"
done
echo "Services Eden arrêtés"
