#!/bin/sh

logHelper() {
	# Expects $1 to be the message itself.
	# Expects $2 to be either "INFO", "OK", "WARNING", "ERROR", or "FATAL". Defaults to "INFO" in case this is empty
	case "$2" in
	"OK")
		PREFIX="\e[0;32m[OK]"
	;;

	"WARNING")
		PREFIX="\e[0;33m[WARNING]"
	;;

	"ERROR")
		PREFIX="\e[0;31m[ERROR]"
	;;

	"FATAL")
		PREFIX="\e[1;31m[FATAL]"
	;;

	*)
    	PREFIX="[INFO]"
    ;;
	esac
	printf "$PREFIX %s\033[0m\n" "$1"
}
checkSudoers() {
	# $1 is expected to be the file's path

    if ! sudo visudo -f "$1" -OPc; then
    	logHelper "Ran into an error while checking the sudoers file" "FATAL"
    	exit 1
    fi
}

printf "Welcome! This script will ensure your system environment is ready to use as many of Bayt's features as possible.\nEverything that will be done here is entirely reversible using CleanupBayt.sh in case you change your mind.\n\n"
if [ "$SETUP_NOCONFIRM" != "1" ]; then
    printf "This process is \e[0;33muser-specific\033[0m. Are you sure you want to set Bayt up for the user '\e[0;32m%s\033[0m'?\nDo note that \e[4;37mthis will allow the user to shutdown and reboot this machine at will\033[0m.\nPress enter to continue, or Ctrl+C to cancel\n" "$USER"
	read -r _
fi

logHelper "Checking if the user has the required permissions..."
if sudo test ! -f "/etc/sudoers.d/10-BaytApi-$USER"; then
	logHelper "Granting special permissions to the user..."

    touch "10-BaytApi"
    echo "$USER ALL = NOPASSWD: /sbin/poweroff, /sbin/reboot" > 10-BaytApi
    sudo chown root:root "10-BaytApi"
    sudo chmod 0440 "10-BaytApi"
    checkSudoers "10-BaytApi"

    sudo mv "10-BaytApi" "/etc/sudoers.d/10-BaytApi-$USER"
    checkSudoers "/etc/sudoers.d/10-BaytApi-$USER"
    logHelper "Permissions were granted successfully!" "OK"

else
	logHelper "Seems like the user already has special permissions" "OK"
fi

logHelper "You should now be able to run Bayt using this user. Have fun!" "OK"