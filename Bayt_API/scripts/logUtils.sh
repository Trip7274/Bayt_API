#!/bin/sh

# This is only intended to be sourced into the other scripts to provide the `logHelper` function.

if [ -z "$BAYT_SCRIPT_LOG_PATH" ]; then
	BAYT_SCRIPT_LOG_PATH="logs/unknown.log"
fi

if [ ! -d "$(dirname "$BAYT_SCRIPT_LOG_PATH")" ]; then
    mkdir "$(dirname "$BAYT_SCRIPT_LOG_PATH")"
fi

if [ ! -f "$BAYT_SCRIPT_LOG_PATH" ]; then
    touch "$BAYT_SCRIPT_LOG_PATH"
fi

logHelper() {
	# Truncate the log file if it's over 10K lines
	if [ "$(wc -l < "$BAYT_SCRIPT_LOG_PATH")" -gt "10000" ]; then
	    tail -c 8500 "$BAYT_SCRIPT_LOG_PATH" > "$BAYT_SCRIPT_LOG_PATH.tmp" && mv "$BAYT_SCRIPT_LOG_PATH.tmp" "$BAYT_SCRIPT_LOG_PATH"
	fi

	LOG_MESSAGE="$1"
	STDOUT="$2"

	echo "[$(date +%c)] $LOG_MESSAGE" | tee -a "$BAYT_SCRIPT_LOG_PATH" >&2

	if [ "$STDOUT" = "stdout" ]; then
	    echo "$LOG_MESSAGE"
	fi
}