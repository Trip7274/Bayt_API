#!/bin/bash

STAT="$1"
PCIID="$2"

# Expected output: "GPU Brand [string]|GPU Name [string?]|Graphics Util Perc [float?]|VRAM Util Perc [float?]|VRAM Total Bytes [ulong?]|VRAM Used Bytes [ulong?]|Encoder Util [float?]|Decoder Util [float?]|Video Enhance Util [float?]|Graphics Frequency [float?]|Encoder/Decoder Frequency [float?]|Power Usage [float?]|TemperatureC [sbyte?]"
# The only REQUIRED output is the GPU Brand, everything else can be "null". If the GPU Name is reported as null, the GPU will be reported as missing.
# When the $STAT is "gpu_ids", the expected output is a newline-seperated list of PCI IDs. For example:
# """
# 03:00.0
# 13:00.0
# """
# If the $STAT is NOT "gpu_ids", then it is expected that the specific GPU ID is passed as $PCIID
#
# $STAT can be:
#
# GPU Brand = gpu_brand (NVIDIA, Intel, AMD, Virtio)
# GPU Name = gpu_name (NVIDIA, Intel [Basic], AMD, Virtio [Basic])
# PCI IDs of every GPU = gpu_ids (All)
#
# Graphics Util (%) = utilization.gpu (NVIDIA, Intel [3D Util], AMD)
# Graphics Frequency (MHz) = clocks.current.graphics (NVIDIA, Intel [Overall freq.], AMD)
#
# VRAM Total (Bytes) = memory.total (AMD, NVIDIA)
# VRAM Used (Bytes) = memory.used (AMD, NVIDIA)
# VRAM Util (%) = utilization.memory (NVIDIA)
#
# GPU Encoder Util (%) = utilization.encoder (NVIDIA, Intel ["Video" Engine], AMD [Average of Enc+Dec])
# GPU Decoder Util (%) = utilization.decoder (NVIDIA)
# "VideoEnhance" Engine Util (%) = utilization.videoenhance (Intel)
# Video Encoder/Decoder Frequency (MHz) = clocks.current.video (NVIDIA)
#
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

logHelper "STAT: '$STAT', PCI ID: '$PCIID'"

# --- Logging setup done ---

[ "$STAT" = "All" ] && STAT=""

BRAND="$(lspci | grep ' VGA ' | grep "$PCIID" | grep -o -E -- "(NVIDIA)?(AMD)?(Intel)?(Virtio)?")"

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
elif [ "$STAT" = "gpu_brand" ]; then
	logHelper "GPU Brand requested. Returning '$BRAND'"
    logHelper "$BRAND" "stdout"

    logHelper "---Exiting (OK)---"
    exit 0
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

			"gpu_name")
				nvidia-smi --query-gpu="$STAT" --format=csv,noheader,nounits --id="$PCIID"
			;;

     		"utilization.gpu")
     			echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?GPU\s+:\s*\K\d+'
     		;;

   			"utilization.memory")
   				UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Memory\s+:\s*\K\d+ '| tr -d '\0')"
                echo "${UNTRIMMED_OUTPUT%% *}"
   			;;

			"memory.total")
				(( FINAL_VALUE = "$(echo "$OUTPUT" | grep -oPz '(?s)FB Memory Usage.*?Total\s+:\s*\K\d+' | tr -d '\0')" * 1048576 ))

				echo "$FINAL_VALUE"
			;;

			"memory.used")
				(( FINAL_VALUE = "$(echo "$OUTPUT" | grep -oPz '(?s)FB Memory Usage.*?Used\s+:\s*\K\d+' | tr -d '\0')" * 1048576 ))

                echo "$FINAL_VALUE"
			;;

     		"utilization.encoder")
     			echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Encoder\s+:\s*\K\d+'
     		;;

   			"utilization.decoder")
     			echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Decoder\s+:\s*\K\d+'
     		;;

        	"utilization.videoenhance")
        		echo "null"
        	;;

     		"clocks.current.graphics")
     			UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Clocks.*?Graphics\s+:\s*\K\d+ '| tr -d '\0')"
				echo "${UNTRIMMED_OUTPUT%% *}"
     		;;

   			"clocks.current.video")
   				UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Clocks.*?Video\s+:\s*\K\d+ '| tr -d '\0')"
                echo "${UNTRIMMED_OUTPUT%% *}"
   			;;

  			"power.draw")
  				 echo "$OUTPUT" | grep -oPz '(?s)Power Samples.*?Avg\s+:\s*\K\d+.?\d*'
  			;;

 			"temperature.gpu")
 				echo "$OUTPUT" | grep -oPz '(?s)Temperature.*?GPU Current Temp\s+:\s*\K\d+'
 			;;

        	*)
        		echo "null"
        	;;
        esac
	}

	if ! nvidia-smi --version > /dev/null; then
		logHelper "nvidia-smi check failed. Make sure it's installed. (Try running 'nvidia-smi --version' in a terminal?)"
		return
	fi

	OUTPUT="$(nvidia-smi -q -d MEMORY,UTILIZATION,TEMPERATURE,ENCODER_STATS,POWER,CLOCK | tail -n +10)"

	CATTEDOUTPUT=""
	if [ "$STAT" != "" ]; then
		logHelper "Specific stat requested, returning '$STAT'"

	    CATTEDOUTPUT=$(getNvidiaStat | tr -d '\0')
	else
		logHelper "All stats requested, returning array"

        for i in "${POSSIBLESTATS[@]}"; do
        	STAT="$i"
        	CATTEDOUTPUT+="$(getNvidiaStat | tr -d '\0')|"
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
                echo "$OUTPUT" | jq '.[0]["Sensors"]["Average Power"]["value"]'
            ;;

        	"temperature.gpu")
        		echo "$OUTPUT" | jq '.[0]["Sensors"]["Edge Temperature"]["value"]'
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
	OUTPUT=$(amdgpu_top -d --pci 0000:"$PCIID" --single --json)

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
		logHelper "intel_gpu_top seems to not have the CAP_PERFMON capability set or haven't been installed. Null data will be returned."
	    return
	fi


	logHelper "Fetching intel_gpu_top output for '$PCIID' device"
	# intel_gpu_top doesn't have a one-shot mode for some reason, so we have to limit it
	OUTPUT=$(timeout 0.1 intel_gpu_top -J -d sys:/sys/devices/pci0000:00/0000:"$PCIID")
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

getVirtio() {
	CATTEDOUTPUT="Virtio|Virtual GPU|null|null|null|null|null|null|null|null|null|null|null"

	logHelper "$CATTEDOUTPUT" "stdout"
    logHelper "---Exiting (OK)---"
    exit 0
}

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

	"Virtio")
		logHelper "Chose Virtio branch"
		getVirtio
	;;
esac

for i in "${POSSIBLESTATS[@]}"; do
    CATTEDOUTPUT+="null|"
done

# TODO: Output in CBOR/MessagePack for easier parsing and more readable code in C#
echo "$CATTEDOUTPUT"

logHelper "Was unable to fetch any stats and returned nulls for device '$PCIID' of the brand '$BRAND'"
logHelper "---Exiting (Fail)---"