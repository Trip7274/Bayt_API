#!/bin/sh

TARGETPATH="$1"
STAT="$2"

# $TARGETPATH can either be an actual path on the filesystem (e.g. "/home"), or a device file path (e.g. "/dev/nvme0n1p3"). Check below for conditions.
# $STAT can be:
#
# "Device.Path" to fetch the device file path from a specific path on the filesystem. Example: "/home" results in "/dev/nvme0n1p3" [string]
# $TARGETPATH is expected to be a path in the file system in this branch. ^
#
# "Device.Filesystem" for the type of filesystem the parititon uses. Example: "/dev/nvme0n1p3" results in "btrfs". [string]
# $TARGETPATH is expected to be a device file path in this branch. ^

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
