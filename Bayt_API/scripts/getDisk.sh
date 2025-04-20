#!/bin/sh

TARGETPATH="$1"
STAT="$2"

[ ! -e "$TARGETPATH" ] && echo null && exit 01
[ "$STAT" = "" ] && echo null && exit 02


case "$STAT" in
	"Device.Filesystem")
		# Please ensure that the TARGETPATH is a device file path in this branch
		df "$TARGETPATH" -T | grep -oP "/dev/[a-zA-Z0-9]+\s+\K[[:alnum:]]+"
	;;

	"Device.Path")
		df "$TARGETPATH" | grep -oE '/dev/[a-zA-Z0-9]+'
	;;
esac
