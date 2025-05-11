#!/bin/bash

# STAT can be:
#
# GPU Brand = gpu_brand (NVIDIA, Intel, AMD)
# GPU Name = gpu_name (NVIDIA, Intel [Basic], AMD)
# PCI IDs of every GPU = gpu_ids (All)
#
# Graphics Util (%) = utilization.gpu (NVIDIA, Intel [3D Util], AMD [Graphics])
#
# VRAM Total (Bytes) = memory.total (AMD)
# VRAM Used (Bytes) = memory.used (AMD)
# VRAM Util (%) = utilization.memory (NVIDIA)
#
# GPU Encoder Util (%) = utilization.encoder (NVIDIA, Intel ["Video" Engine], AMD ["MediaEngine"])
# GPU Decoder Util (%) = utilization.decoder (NVIDIA)
# "VideoEnhance" Engine Util (%) = utilization.videoenhance (Intel)
#
# Graphics Frequency (MHz) = clocks.current.graphics (NVIDIA, Intel [Overall freq.], AMD)
# Video Encoder/Decoder Frequency (MHz) = clocks.current.video (NVIDIA)
# Power draw (W) = power.draw (NVIDIA, Intel, AMD)
# Temperature (C) = temperature.gpu (NVIDIA, AMD)

LOGPATH="logs/GPU.log"
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

logHelper "---getGpu.sh started---"

STAT="$1"
PCIID="$2"
logHelper "STAT: '$STAT', PCI ID: '$PCIID'"

[ "$STAT" = "All" ] && STAT=""

if [ "$STAT" = "gpu_ids" ]; then
    IDS="$(lspci | grep ' VGA ' | grep -oE -- "[0-9]+:[0-9]+.[0-9]")"
    logHelper "PCI ID list was requested. Returning array"

    logHelper "$IDS" "stdout"
    logHelper "---Exiting (OK)---"
    exit 0

elif [ "$PCIID" = "" ]; then
	logHelper "No PCI ID was provided, unable to continue"
	logHelper "---Exiting (Fail 01)---"
    exit 01
fi

POSSIBLESTATS=("gpu_brand" "gpu_name" "utilization.gpu" "utilization.memory" "memory.total" "memory.used" \
				"utilization.encoder" "utilization.decoder" "utilization.videoenhance" "clocks.current.graphics" \
				"clocks.current.video" "power.draw" "temperature.gpu")


getNvidia() {
	getNvidiaStat() {
		case $STAT in
			"gpu_brand")
				echo "NVIDIA"
			;;

        	"utilization.videoenhance" | "memory.total" | "memory.used")
        		echo null
        	;;

        	*)
        		nvidia-smi --query-gpu="$STAT" --format=csv,noheader,nounits --id="$PCIID"
        	;;
        esac
	}

	if ! nvidia-smi --version > /dev/null; then
		logHelper "nvidia-smi check failed. Make sure it's installed. (Try running 'nvidia-smi --version' in a terminal?)"
		return
	fi


	CATTEDOUTPUT=""
	if [ "$STAT" != "" ]; then
		logHelper "Specific stat requested, returning '$STAT'"

	    CATTEDOUTPUT=$(getNvidiaStat)
	else
		logHelper "All stats requested, returning array"

        for i in "${POSSIBLESTATS[@]}"; do
        	STAT="$i"
        	CATTEDOUTPUT+="$(getNvidiaStat)|"
        done
	fi

	logHelper "$CATTEDOUTPUT" "stdout"
	logHelper "---Exiting (OK)---"
    exit 0
}

getAmd() {
	getAmdStat() {
		case $STAT in
            "gpu_brand")
                echo "AMD"
            ;;

            "gpu_name")
                echo "$OUTPUT" | jq '.[0]["DeviceName"]'
            ;;

            "utilization.gpu")
            	echo "$OUTPUT" | jq '.[0]["gpu_activity"]["GFX"]["value"]'
            ;;

        	"memory.total") # Convert from source MiB to Bytes, in case it wasnt clear
        		(( FINAL_VALUE="$(echo "$OUTPUT" | jq '.[0]["VRAM"]["Total VRAM"]["value"]')" * 1048576 ))
        		echo "$FINAL_VALUE"
        	;;

     		"memory.used")
        		(( FINAL_VALUE="$(echo "$OUTPUT" | jq '.[0]["VRAM"]["Total VRAM Usage"]["value"]')" * 1048576 ))
        		echo "$FINAL_VALUE"
        	;;

            "utilization.encoder")
            	echo "$OUTPUT" | jq '.[0]["gpu_activity"]["MediaEngine"]["value"]'
            ;;

            "clocks.current.graphics")
            	echo "$OUTPUT" | jq '.[0]["Sensors"]["GFX_SCLK"]["value"]'
            ;;

            "power.draw")
                echo "$OUTPUT" | jq '.[0]["Sensors"]["GFX Power"]["value"]'
            ;;

        	"temperature.gpu")
        		echo "$OUTPUT" | jq '.[0]["Sensors"]["CPU Tctl"]["value"]'
        	;;

            *)
            	echo null
            ;;
        esac
	}

	if ! amdgpu_top -V > /dev/null; then
		logHelper "amdgpu_top check failed. Make sure it's installed. (Try running 'amdgpu_top -V' in a terminal?)"
		return
	fi


	logHelper "Fetching amdgpu_top output for '$PCIID' device"
	OUTPUT=$(amdgpu_top -d --pci 0000:"$PCIID" --json)

	CATTEDOUTPUT=""
    if [ "$STAT" != "" ]; then
    	logHelper "Specific stat requested, returning '$STAT'"

        CATTEDOUTPUT=$(getAmdStat)
    else
    	logHelper "All stats requested, returning array"

    	for i in "${POSSIBLESTATS[@]}"; do
    		STAT="$i"
    		CATTEDOUTPUT+="$(getAmdStat)|"
    	done
    fi

	logHelper "$CATTEDOUTPUT" "stdout"
	logHelper "---Exiting (OK)---"
	exit 0
}

getIntel() {
	getIntelStat() {
    	case $STAT in
    		"gpu_brand")
            	echo "Intel"
            ;;

    		"gpu_name")
                echo "Intel GPU"
            ;;

            "utilization.gpu")
            	echo "$OUTPUT" | jq '.["engines"]["Render/3D"]["busy"]'
            ;;

            "utilization.encoder")
            	echo "$OUTPUT" | jq '.["engines"]["Video"]["busy"]'
            ;;

            "utilization.videoenhance")
            	echo "$OUTPUT" | jq '.["engines"]["VideoEnhance"]["busy"]'
            ;;

            "clocks.current.graphics")
            	echo "$OUTPUT" | jq '.["frequency"]["actual"]'
            ;;

            "power.draw")
                echo "$OUTPUT" | jq '.["power"]["Package"]'
            ;;

            *)
            	echo null
            ;;
            esac
    	}

	if ! intel_gpu_top -h > /dev/null; then
		logHelper "intel_gpu_top seems to not have the CAP_PERFMON capability set and that you have it installed. Null data will be returned."
	    return
	fi


	logHelper "Fetching intel_gpu_top output for '$PCIID' device"
	OUTPUT=$(timeout 0.5 intel_gpu_top -J -d sys:/sys/devices/pci0000:00/0000:"$PCIID")
    OUTPUT=$(echo "$OUTPUT" | tail -n +3)

	CATTEDOUTPUT=""
	if [ "$STAT" != "" ]; then
		logHelper "Specific stat requested, returning '$STAT'"

        CATTEDOUTPUT=$(getIntelStat)

	else
		logHelper "All stats requested, returning array"

		for i in "${POSSIBLESTATS[@]}"; do
			STAT="$i"
			CATTEDOUTPUT+="$(getIntelStat)|"
		done
	fi

	logHelper "$CATTEDOUTPUT" "stdout"
	logHelper "---Exiting (OK)---"
	exit 0
}


BRAND="$(lspci | grep ' VGA ' | grep "$PCIID" | grep -o -E -- "(NVIDIA)?(AMD)?(Intel)?")"

if [ "$STAT" = "gpu_brand" ]; then
	logHelper "GPU Brand requested. Returning '$BRAND'"
    logHelper "$BRAND" "stdout"

    logHelper "---Exiting (OK)---"
    exit 0
fi

case $BRAND in
	"NVIDIA")
		logHelper "Chose NVIDIA branch"
		getNvidia
	;;

	"AMD")
		logHelper "Chose AMD branch"
		getAmd
	;;

	"Intel")
		logHelper "Chose Intel branch"
		getIntel
	;;
esac

for i in "${POSSIBLESTATS[@]}"; do
    CATTEDOUTPUT+="null|"
done

# TODO: Output in CBOR/MessagePack for easier parsing and more readable code in C#
echo "$CATTEDOUTPUT"

logHelper "Was unable to fetch any stats and returned nulls for device '$PCIID' of the brand '$BRAND'"
logHelper "---Exiting (Fail)---"