#!/bin/sh

STAT="$1"

# The output format varies here, usually being a single number if the $STAT wasn't "AllUtil". Consult the list below for specific types and documentation
#
# $STAT can be:
# "Name" for the CPU's name                                       												[string]
# "UtilPerc" for the CPU's utilization percentage (over all cores)												[float]
# "PhysicalCores" for the physical CPU core count                 												[ushort]
# "ThreadCount" for the amount of logical CPU cores (AKA threads) 												[ushort]
# "Temperature" for the average temperature of the CPU, the first element will be a string describing 			[string?|float?]
# the source of the temperature reading, while the second will be the actual reading.
#
# "AllUtil" for a list with the format: "UtilPerc|PhysicalCores|ThreadCount|TempType|TempValue"

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

logHelper "---getCpu.sh started---"

# --- Logging setup done ---

if [ "$STAT" = "" ]; then
	logHelper "STAT not provided, exiting..."
	logHelper "---Exiting (Fail 01)---"

	exit 1
fi

getName() {
	NAME="$(lscpu | grep -oP 'Model name:\s+\K.*')"
    logHelper "Name requested, returned '$NAME'"

    logHelper "$NAME" "stdout"
}

getUtil() {
	FIRSTFETCH="$(grep 'cpu ' /proc/stat)"
	SECONDFETCH="$(sleep 0.1 && grep 'cpu ' /proc/stat)"
	UTILPERC="$(echo "$FIRSTFETCH $SECONDFETCH" | awk -v RS="" '{print ($13-$2+$15-$4)*100/($13-$2+$15-$4+$16-$5)}')"
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

getTemps() {
	TEMPTYPE="null"
	TEMPVAL="null"

	for zone in /sys/class/thermal/thermal_zone*; do
		if [ "$(cat "$zone/type")" = "x86_pkg_temp" ]; then
		    TEMPVAL="$(cat "$zone/temp")"
			TEMPTYPE="thermalZone"
		fi
	done

	if [ "$TEMPTYPE" = "null" ] && sensors -v > /dev/null; then
	    sensorsOutput="$(sensors -j)"

	    amdCheck="$(echo "$sensorsOutput" | jq '.["k10temp-pci-00c3"]["Tctl"]["temp1_input"]')"
	    intelCheck="$(echo "$sensorsOutput" | jq '.["coretemp-isa-0000"]["Package id 0"]["temp1_input"]')"
	    if [ "$amdCheck" != "null" ]; then
	        TEMPVAL="$amdCheck"
	        TEMPTYPE="sensors-lm"
	    elif [ "$intelCheck" != "null" ]; then
	        TEMPVAL="$intelCheck"
	        TEMPTYPE="sensors-lm"
	    fi
	fi

	logHelper "Temperatures requested, returned '$TEMPTYPE|$TEMPVAL'"
	logHelper "$TEMPTYPE|$TEMPVAL" "stdout"
}


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

	"Temperature")
		logHelper "Temperatures requested, choosing that branch."
		getTemps
	;;

	"AllUtil")
		logHelper "All stats requested, returning list"
		logHelper "$(getUtil)|$(getPhysicalCores)|$(getThreads)|$(getTemps)" "stdout"
	;;

	*)
		logHelper "'$STAT' requested, returning null as it isn't recognized"
		logHelper "null" "stdout"
	;;
esac


logHelper "---Exiting (OK)---"