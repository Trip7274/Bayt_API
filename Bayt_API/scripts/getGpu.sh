#!/usr/bin/env bash

STAT="$1"
PCIID="$2"

# Execution format: ./getGpu.sh "$STAT" "$PCIID"
# Expected output: "GPU Brand [string]|GPU Name [string?]|GPU Type [Bool?]|Graphics Util Perc [float?]|VRAM Util Perc [float?]|VRAM Total Bytes [ulong?]|VRAM Used Bytes [ulong?]|Encoder Util [float?]|Decoder Util [float?]|Video Enhance Util [float?]|Graphics Frequency [float?]|Encoder/Decoder Frequency [float?]|Power Usage [float?]|TemperatureC [sbyte?]|FanSpeedRPM [ushort?]"
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
# GPU Name = gpu_name (NVIDIA, Intel {NVTOP}, AMD, Virtio [Basic])
# GPU Type (isDedicatedGpu?) = gpu.dedicated (AMD)
# PCI IDs of every GPU = gpu_ids (All)
#
# Graphics Util (%) = utilization.gpu (NVIDIA, Intel [3D Util, highly unstable], AMD)
# Graphics Frequency (MHz) = clocks.current.graphics (NVIDIA, Intel [Overall freq. {NVTOP}], AMD)
#
# VRAM Total (Bytes) = memory.total (AMD, NVIDIA)
# VRAM Used (Bytes) = memory.used (AMD, NVIDIA)
# VRAM Util (%) = utilization.memory (NVIDIA)
# VRAM GTT Util (%) = memory.gtt.util (AMD)
#
# GPU Encoder Util (%) = utilization.encoder (NVIDIA, Intel ["Video" Engine], AMD [Average of Enc+Dec])
# GPU Decoder Util (%) = utilization.decoder (NVIDIA)
# "VideoEnhance" Engine Util (%) = utilization.videoenhance (Intel)
# Video Encoder/Decoder Frequency (MHz) = clocks.current.video (NVIDIA)
#
# Power draw (W) = power.draw (NVIDIA, Intel, AMD [dGPU only, otherwise CPU draw])
# Power Cap (W) = power.cap (AMD [dGPU only])
# Temperature (C) = temperature.gpu (NVIDIA, AMD)
# Fan Speed (RPM) = fan.speed (AMD)

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

LSPCIOUTPUT="$(lspci | grep ' VGA ')"
BRAND="$(echo "$LSPCIOUTPUT" | grep "$PCIID" | grep -o -E -- "(NVIDIA)?(AMD)?(Intel)?(Virtio)?")"

if [ "$STAT" = "gpu_ids" ]; then
    IDS="$(echo "$LSPCIOUTPUT" | grep -oE -- "[0-9]+:[0-9]+\.[0-9]+(\.[0-9]+)?")"
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

declare -A gpuStats=(
	["gpu_brand"]="null"
	["gpu_name"]="null"
	["gpu.dedicated"]="null"
	["utilization.gpu"]="null"
	["clocks.current.graphics"]="null"
	["utilization.memory"]="null"
	["memory.total"]="null"
	["memory.used"]="null"
	["memory.gtt.util"]="null"
	["utilization.encoder"]="null"
	["utilization.decoder"]="null"
	["utilization.videoenhance"]="null"
	["clocks.current.video"]="null"
	["power.draw"]="null"
	["power.cap"]="null"
	["temperature.gpu"]="null"
	["fan.speed"]="null"
)


getNvidia() {
	getNvidiaStat() {
		case "$1" in
			"gpu_brand")
				gpuStats["gpu_brand"]="NVIDIA"
			;;

			"gpu_name")
				gpuStats["gpu_name"]="$(nvidia-smi --query-gpu="gpu_name" --format=csv,noheader,nounits --id="$PCIID")"
			;;

     		"utilization.gpu")
     			gpuStats["utilization.gpu"]="$(echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?GPU\s+:\s*\K\d+')"
     		;;

   			"utilization.memory")
   				UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Memory\s+:\s*\K\d+ '| tr -d '\0')"
                gpuStats["utilization.memory"]="${UNTRIMMED_OUTPUT%% *}"
   			;;

			"memory.total")
				(( FINAL_VALUE = "$(echo "$OUTPUT" | grep -oPz '(?s)FB Memory Usage.*?Total\s+:\s*\K\d+' | tr -d '\0')" * 1048576 ))

				gpuStats["memory.total"]="$FINAL_VALUE"
			;;

			"memory.used")
				(( FINAL_VALUE = "$(echo "$OUTPUT" | grep -oPz '(?s)FB Memory Usage.*?Used\s+:\s*\K\d+' | tr -d '\0')" * 1048576 ))

				gpuStats["memory.used"]="$FINAL_VALUE"
			;;

     		"utilization.encoder")
     		     gpuStats["utilization.encoder"]="$(echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Encoder\s+:\s*\K\d+')"
     		;;

   			"utilization.decoder")
   				gpuStats["utilization.decoder"]="$(echo "$OUTPUT" | grep -oPz '(?s)Utilization.*?Decoder\s+:\s*\K\d+')"
     		;;

     		"clocks.current.graphics")
     			UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Clocks.*?Graphics\s+:\s*\K\d+ '| tr -d '\0')"
				gpuStats["clocks.current.graphics"]="${UNTRIMMED_OUTPUT%% *}"
     		;;

   			"clocks.current.video")
   				UNTRIMMED_OUTPUT="$(echo "$OUTPUT" | grep -oPz '(?s)Clocks.*?Video\s+:\s*\K\d+ '| tr -d '\0')"
   				gpuStats["clocks.current.video"]="${UNTRIMMED_OUTPUT%% *}"
   			;;

  			"power.draw")
  				gpuStats["power.draw"]="$(echo "$OUTPUT" | grep -oPz '(?s)Power Samples.*?Avg\s+:\s*\K\d+.?\d*')"
  			;;

 			"temperature.gpu")
 				gpuStats["temperature.gpu"]="$(echo "$OUTPUT" | grep -oPz '(?s)Temperature.*?GPU Current Temp\s+:\s*\K\d+')"
 			;;

        	*)
                [[ -n "${gpuStats["$1"]}" ]] && gpuStats["$1"]="null"
        	;;
        esac
	}

	if ! nvidia-smi --version > /dev/null; then
		logHelper "nvidia-smi check failed. Make sure it's installed. (Try running 'nvidia-smi --version' in a terminal?)"
		return
	fi

	OUTPUT="$(nvidia-smi -q -d MEMORY,UTILIZATION,TEMPERATURE,ENCODER_STATS,POWER,CLOCK | tail -n +10)"

	if [ "$STAT" != "" ]; then
		logHelper "Specific stat requested, fetching '$STAT'"

		getNvidiaStat "$STAT"
	else
		logHelper "All stats requested, returning array"

		for i in "${!gpuStats[@]}"; do
			getNvidiaStat "$i"
		done
	fi
}

getAmd() {
	getAmdStat() {
		case "$1" in
            "gpu_brand")
                gpuStats["gpu_brand"]="AMD"
            ;;

            "gpu_name")
            	gpuStats["gpu_name"]="$(echo "$OUTPUT" | jq -r '.[0]["DeviceName"]')"
            ;;

        	"gpu.dedicated")
        		TYPE="$(echo "$OUTPUT" | jq -r '.[0]["GPU Type"]')"

        		[[ $TYPE == "dGPU" ]] && gpuStats["gpu.dedicated"]="true" || gpuStats["gpu.dedicated"]="false"
        	;;

            "utilization.gpu")
            	gpuStats["utilization.gpu"]="$(echo "$OUTPUT" | jq '.[0]["gpu_activity"]["GFX"]["value"]')"
            ;;

        	"memory.total") # Convert from source MiB to Bytes, in case it wasnt clear
        		(( FINAL_VALUE="$(echo "$OUTPUT" | jq '.[0]["VRAM"]["Total VRAM"]["value"]')" * 1048576 ))

        		gpuStats["memory.total"]="$FINAL_VALUE"
        	;;

     		"memory.used")
        		(( FINAL_VALUE="$(echo "$OUTPUT" | jq '.[0]["VRAM"]["Total VRAM Usage"]["value"]')" * 1048576 ))
        		gpuStats["memory.used"]="$FINAL_VALUE"
        	;;

     		"memory.gtt.util")
     			gpuStats["memory.gtt.util"]="$(echo "$OUTPUT" | jq '.[0]["gpu_activity"]["Memory"]["value"]')"
     		;;

            "utilization.encoder")
            	gpuStats["utilization.encoder"]="$(echo "$OUTPUT" | jq '.[0]["gpu_activity"]["MediaEngine"]["value"]')"
            ;;

            "clocks.current.graphics")
            	gpuStats["clocks.current.graphics"]="$(echo "$OUTPUT" | jq '.[0]["Sensors"]["GFX_SCLK"]["value"]')"
            ;;

            "power.draw")
                possiblePower=$(echo "$OUTPUT" | jq '.[0]["Sensors"]["Average Power"]["value"]')
            if [[ $possiblePower == "null" ]]; then
            	possiblePower=$(echo "$OUTPUT" | jq '.[0]["Sensors"]["Input Power"]["value"]')
            fi

            gpuStats["power.draw"]="$possiblePower"
            ;;

        	"power.cap")
            	gpuStats["power.cap"]="$(echo "$OUTPUT" | jq '.[0]["Power Cap"]["current"]')"
            ;;

        	"temperature.gpu")
        		gpuStats["temperature.gpu"]="$(echo "$OUTPUT" | jq '.[0]["Sensors"]["Edge Temperature"]["value"]')"
        	;;

     		"fan.speed")
     		if [[ "$(echo "$OUTPUT" | jq '.[0]["Sensors"]["Fan"]')" != "null" ]]; then
     		    gpuStats["fan.speed"]="$(echo "$OUTPUT" | jq '.[0]["Sensors"]["Fan"]["value"]')"
     		fi
     		;;

            *)
            	gpuStats["$1"]="null"
            ;;
        esac
	}

	if ! amdgpu_top -V > /dev/null; then
		logHelper "amdgpu_top check failed. Make sure it's installed. (Try running 'amdgpu_top -V' in a terminal?)"
		return
	fi


	logHelper "Fetching amdgpu_top output for '$PCIID' device"
	OUTPUT=$(amdgpu_top -d --pci 0000:"$PCIID" --single --json)

    if [ "$STAT" != "" ]; then
    	logHelper "Specific stat requested, fetching '$STAT'"

        getAmdStat "$STAT"
    else
    	logHelper "All stats requested"

    	for i in "${!gpuStats[@]}"; do
        	getAmdStat "$i"
        done
    fi
}

getIntel() {
	getIntelStat() {
    	case "$1" in
    		"gpu_brand")
    			gpuStats["gpu_brand"]="Intel"
            ;;

    		"gpu_name")
    			# I wish we could specify the GPU, or figure it out, but alas, we must just hope that the user only has one
                gpuStats["gpu_name"]="$(echo "$OUTPUTNVTOP" | jq '.[0]["device_name"]')"
            ;;

            "utilization.gpu")
            	gpuStats["utilization.gpu"]="$(echo "$OUTPUT" | jq '.["engines"]["Render/3D"]["busy"]')"
            ;;

            "utilization.encoder")
                gpuStats["utilization.encoder"]="$(echo "$OUTPUT" | jq '.["engines"]["Video"]["busy"]')"
            ;;

            "utilization.videoenhance")
           		gpuStats["utilization.videoenhance"]="$(echo "$OUTPUT" | jq '.["engines"]["VideoEnhance"]["busy"]')"
            ;;

            "clocks.current.graphics")
            	gpuStats["clocks.current.graphics"]="$(echo "$OUTPUTNVTOP" | jq '.[0]["gpu_clock"]' | grep -o "[0-9]*")"
            ;;

            "power.draw")
            	gpuStats["power.draw"]="$(echo "$OUTPUT" | jq '.["power"]["Package"]')"
            ;;

            *)
                [[ -n "${gpuStats["$1"]}" ]] && gpuStats["$1"]="null"
            ;;
            esac
    	}

	if ! intel_gpu_top -h > /dev/null; then
		logHelper "intel_gpu_top seems to not have the CAP_PERFMON capability set or haven't been installed. Null data will be returned."
	    return
	fi
	if ! nvtop -v > /dev/null; then
		logHelper "NVTOP seems to be missing or not setup correctly. Null data will be returned."
	    return
	fi


	logHelper "Fetching intel_gpu_top output for '$PCIID' device"
	# intel_gpu_top doesn't have a one-shot mode for some reason, so we have to limit it
	OUTPUT="$(timeout 0.1 intel_gpu_top -J -d sys:/sys/devices/pci0000:00/0000:"$PCIID")"
    OUTPUT="$(echo "$OUTPUT" | tail -n +3)"
    logHelper "Fetching NVTOP output for '$PCIID' device"
    OUTPUTNVTOP="$(nvtop -s)"

	if [ "$STAT" != "" ]; then
		logHelper "Specific stat requested, returning '$STAT'"

        getIntelStat "$STAT"

	else
		logHelper "All stats requested, returning array"

		for i in "${!gpuStats[@]}"; do
			getIntelStat "$i"
		done
	fi
}

getVirtio() {
	gpuStats["gpu_brand"]="Virtio"
	gpuStats["gpu_name"]="Virtual GPU"
}

case $BRAND in
	"NVIDIA")
		logHelper "Chose NVIDIA branch"
		getNvidia
		logHelper "Successfully fetched and processed NVIDIA GPU stats."
	;;

	"AMD")
		logHelper "Chose AMD branch"
		getAmd
		logHelper "Successfully fetched and processed AMD GPU stats."
	;;

	"Intel")
		logHelper "Chose Intel branch"
		getIntel
		logHelper "Successfully fetched and processed Intel GPU stats."
	;;

	"Virtio")
		logHelper "Chose Virtio branch"
		getVirtio
		logHelper "Successfully fetched and processed Virtio GPU stats."
	;;
esac

jqArgs=("-n")
[[ "$BAYT_SUBPROCESS" == "1" ]] && logHelper "Running under Bayt. Returning compact JSON." && jqArgs+=("-c" "-M")

jq "${jqArgs[@]}"                                                                                 \
--arg "Brand"                          "${gpuStats["gpu_brand"]:="Unknown"}"                      \
--arg "Name"                           "${gpuStats["gpu_name"]:="Unknown"}"                       \
--argjson "isDedicated"                "${gpuStats["gpu.dedicated"]:="null"}"                     \
--argjson "Graphics Utilization"       "${gpuStats["utilization.gpu"]:="null"}"                   \
--argjson "Graphics Frequency"         "${gpuStats["clocks.current.graphics"]:="null"}"           \
--argjson "VRAM Utilization"           "${gpuStats["utilization.memory"]:="null"}"                \
--argjson "VRAM Total Bytes"           "${gpuStats["memory.total"]:="null"}"                      \
--argjson "VRAM Used Bytes"            "${gpuStats["memory.used"]:="null"}"                       \
--argjson "VRAM GTT Utilization"       "${gpuStats["utilization.gtt.util"]:="null"}"              \
--argjson "Encoder Utilization"        "${gpuStats["utilization.encoder"]:="null"}"               \
--argjson "Decoder Utilization"        "${gpuStats["utilization.decoder"]:="null"}"               \
--argjson "VideoEnhance Utilization"   "${gpuStats["utilization.videoenhance"]:="null"}"          \
--argjson "EncDec Frequency"           "${gpuStats["clocks.current.video"]:="null"}"              \
--argjson "Power Draw"                 "${gpuStats["power.draw"]:="null"}"                        \
--argjson "Power Cap"                  "${gpuStats["power.cap"]:="null"}"                         \
--argjson "Temperature"                "${gpuStats["temperature.gpu"]:="null"}"                   \
--argjson "Fan Speed"                  "${gpuStats["fan.speed"]:="null"}"                         \
'$ARGS.named'

logHelper "--- Exiting ---"