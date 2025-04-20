#!/bin/sh

OPERATION=$1

[ "$OPERATION" = "" ] && exit 1

case $OPERATION in

	# Regex galore!
	"Name")
    	lscpu | grep -oP 'Model name:\s+\K.*'
    ;;

	"UtilPerc")
		top -bn1 | grep "Cpu(s)" | sed "s/.*, *\([0-9.]*\)%* id.*/\1/" | awk '{print 100 - $1}'
    ;;

	"PhysicalCores")
		 grep -oP "cpu cores\s+:\s+\K[0-9]+" < /proc/cpuinfo | head -n 1
	;;

	"ThreadCount")
		nproc --all
	;;

	*)
		echo null
	;;
esac
