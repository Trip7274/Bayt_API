#!/bin/sh

OPERATION=$1

[ "$OPERATION" = "" ] && exit 1

case $OPERATION in

	# Regex galore!
	"Name")
    	lscpu | grep -oP 'Model name:\s+\K.*'
    ;;

	"UtilPerc")
		grep 'cpu ' < /proc/stat | awk '{print ($5*100)/($2+$3+$4+$5+$6+$7+$8+$9+$10)}'| awk '{print 100-$1}'
    ;;

	"PhysicalCores")
		 grep -oP "cpu cores\s+:\s+\K[0-9]+" < /proc/cpuinfo | head -n 1
	;;

	"ThreadCount")
		nproc --all
	;;

	"AllUtil")
		UTILPERC="$(grep 'cpu ' < /proc/stat | awk '{print ($5*100)/($2+$3+$4+$5+$6+$7+$8+$9+$10)}'| awk '{print 100-$1}')"
		PCORES="$(grep -oP "cpu cores\s+:\s+\K[0-9]+" < /proc/cpuinfo | head -n 1)"
		THREADS="$(nproc --all)"

		echo "$UTILPERC|$PCORES|$THREADS"
	;;

	*)
		echo null
	;;
esac
