#!/bin/sh

# Returns in MiBs

STAT="$1"

[ "$STAT" = "" ] && exit 1

SEDEX=""

case $STAT in
  "Total")
    SEDEX="s/.*: *\([0-9.]*\)%* total.*/\1/"
    ;;

  "Free")
	SEDEX="s/.*, *\([0-9.]*\)%* free.*/\1/"
    ;;

  "Used")
    SEDEX="s/.*, *\([0-9.]*\)%* used.*/\1/"
    ;;

  *)
    exit 2
    ;;
esac



top -bn1 | grep "MiB Mem" | sed "$SEDEX"
