#!/usr/bin/env bash

scriptdir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

gamedir=$1
dll=$2

usage() {
  echo "usage: rungodot.sh GODOTGAMEDIR MODDLL [--execpath EXECPATH] [--args args...]"
}

if [ -z $gamedir ] || [ -z $dll ]; then
  usage
  exit 1
fi

shift 2

execpath="$gamedir/*.x86_64"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --execpath)
      shift
      if [[ $# -eq 0 ]]; then
        echo "--execpath REQUIRES A FILENAME" >&2
        exit 1
      fi

      execpath="$1"
      shift
      ;;

    --args)
      shift
      break
      ;;

    *)
      echo "Unknown argument: $1" >&2
      exit 1
      ;;
  esac
done

if [ ! -f $execpath ]; then
  echo "NO FILE FOUND AT $execpath" >&2
  usage
  exit 1
fi

datadir=$(ls -d $gamedir/data*) || exit 1

if [ ! -f $dll ]; then
  echo "NO FILE FOUND AT $dll" >&2
  usage
  exit 1
fi

modname=$(basename -- "$dll")
modname="${modname%.*}"

DONOTLITTER_LIBCORECLR_PATH=$datadir/libcoreclr.so \
DONOTLITTER_ASSEMBLY=$(realpath $dll) \
DONOTLITTER_MOD_NAME=$modname \
LD_PRELOAD=/usr/lib/libgcc_s.so.1:"$scriptdir"/libdonotlitter.so \
$execpath "$@"
