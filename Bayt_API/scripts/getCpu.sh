#!/bin/sh

STAT="$1"

# The output format varies here, usually being a single number if the $STAT wasn't "AllUtil". Consult the list below for specific types and documentation
#
# $STAT can be:
# "Name" for the CPU's name                                                    [string]
# "UtilPerc" for the CPU's utilization percentage (over all cores)             [float]
# "PhysicalCores" for the physical CPU core count                              [ushort]
# "ThreadCount" for the amount of logical CPU cores (AKA threads)              [ushort]
#
# "AllUtil" for a list with the format: "UtilPerc|PhysicalCores|ThreadCount"

LOGPATH="logs/CPU.log"
logHelper(){
	# Truncate the log file if it's over 10K lines
	if [ "$(cat $LOGPATH | wc -l)" -gt "10000" ]; then
	    tail -c 8500 "$LOGPATH" > "$LOGPATH.tmp" && mv "$LOGPATH.tmp" "$LOGPATH"
	fi

	LOGMESSAGE="$1"
	STDOUT="$2"

	echo "[$(date +%c)] $LOGMESSAGE" | tee -a "$LOGPATH" >&2

	if [ "$STDOUT" = "stdout" ]; then
	    echo "$LOGMESSAGE"
	fi
}

if [ ! -d "$(dirname $LOGPATH)" ]; then
    mkdir "$(dirname $LOGPATH)"
fi

if [ ! -f "$LOGPATH" ]; then
    touch "$LOGPATH"
fi

getName() {
	NAME="$(lscpu | grep -oP 'Model name:\s+\K.*')"
    logHelper "Name requested, returned '$NAME'"

    logHelper "$NAME" "stdout"
}

getUtil() {
	UTILPERC="$(grep 'cpu ' < /proc/stat | awk '{print ($5*100)/($2+$3+$4+$5+$6+$7+$8+$9+$10)}'| awk '{print 100-$1}')"
    logHelper "UtilPerc requested, returned '$UTILPERC'"

    logHelper "$UTILPERC" "stdout"
}

getPhysicalCores() {
	PCORES="$(grep -oP "cpu cores\s+:\s+\K[0-9]+" < /proc/cpuinfo | head -n 1)"
    logHelper "PhysicalCores requested, returned: '$PCORES'"

    logHelper "$PCORES" "stdout"
}

getThreads() {
	THREADS="$(nproc --all)"
    logHelper "ThreadCount requested, returned: '$THREADS'"

    logHelper "$THREADS" "stdout"
}

logHelper "---getCpu.sh started---"

if [ "$STAT" = "" ]; then
	logHelper "STAT not provided, exiting..."
	logHelper "---Exiting (Fail 01)---"

	exit 1
fi

case $STAT in
	"Name")
		logHelper "CPU name requested, choosing that branch."
    	getName
    ;;

	"UtilPerc")
		logHelper "CPU utilization requested, choosing that branch."
		getUtil
    ;;

	"PhysicalCores")
		logHelper "CPU physical core count requested, choosing that branch."
		getPhysicalCores
	;;

	"ThreadCount")
		logHelper "CPU thread count requested, choosing that branch."
		getThreads
	;;

	"AllUtil")
		logHelper "All stats requested, returning list"
		logHelper "$(getUtil)|$(getPhysicalCores)|$(getThreads)" "stdout"
	;;

	*)
		logHelper "'$STAT' requested, returning null as it isn't recognized"
		logHelper "null" "stdout"
	;;
esac


logHelper "---Exiting (OK)---"