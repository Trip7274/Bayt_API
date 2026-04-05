#!/bin/sh

# This is only intended to be sourced into the other scripts to provide the `logHelper` function.

if [ -z "$SOFA_SCRIPT_LOG_PATH" ]; then
	SOFA_SCRIPT_LOG_PATH="logs/unknown.log"
fi

if [ ! -d "$(dirname "$SOFA_SCRIPT_LOG_PATH")" ]; then
    mkdir "$(dirname "$SOFA_SCRIPT_LOG_PATH")"
fi

if [ ! -f "$SOFA_SCRIPT_LOG_PATH" ]; then
    touch "$SOFA_SCRIPT_LOG_PATH"
fi

logHelper() {
	# Truncate the log file if it's over 10K lines
	if [ "$(wc -l < "$SOFA_SCRIPT_LOG_PATH")" -gt "10000" ]; then
	    tail -c 8500 "$SOFA_SCRIPT_LOG_PATH" > "$SOFA_SCRIPT_LOG_PATH.tmp" && mv "$SOFA_SCRIPT_LOG_PATH.tmp" "$SOFA_SCRIPT_LOG_PATH"
	fi

	LOG_MESSAGE="$1"
	STDOUT="$2"

	echo "[$(date +%c)] $LOG_MESSAGE" | tee -a "$SOFA_SCRIPT_LOG_PATH" >&2

	if [ "$STDOUT" = "stdout" ]; then
	    echo "$LOG_MESSAGE"
	fi
}