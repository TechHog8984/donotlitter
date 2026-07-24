#!/usr/bin/env bash

gamedir=$1
dll=$2

usage() {
  echo "usage: rungodot.sh GODOTGAMEDIR MODDLL"
}

if [ -z $gamedir ] || [ -z $dll ]; then
  usage;
  exit 1;
fi

datadir=$(ls -d $gamedir/data*) || exit 1

if [ ! -f $dll ]; then
  echo "NO FILE FOUND AT $dll";
  usage;
  exit 1;
fi

modname=$(basename -- "$dll")
modname="${modname%.*}"

DONOTLITTER_LIBCORECLR_PATH=$datadir/libcoreclr.so \
DONOTLITTER_ASSEMBLY=$(realpath $dll) \
DONOTLITTER_MOD_NAME=$modname \
LD_PRELOAD=/usr/lib/libgcc_s.so.1:$(realpath ./libdonotlitter.so) \
$gamedir/*.x86_64
