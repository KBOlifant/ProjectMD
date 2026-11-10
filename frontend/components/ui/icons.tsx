"use client";

import type { ComponentType } from "react";
import {
  ArrowElbowDownLeftIcon,
  ArrowRightIcon as PhArrowRight,
  AtomIcon as PhAtom,
  CheckIcon as PhCheck,
  ClockCounterClockwiseIcon,
  CodeIcon as PhCode,
  CopyIcon as PhCopy,
  CubeIcon as PhCube,
  DiscIcon as PhDisc,
  DownloadSimpleIcon,
  FileTextIcon,
  MagnifyingGlassIcon,
  PaletteIcon as PhPalette,
  PlayIcon,
  ScanIcon as PhScan,
  SparkleIcon,
  SquaresFourIcon,
  TrashIcon as PhTrash,
  TriangleIcon as PhTriangle,
  UserSquareIcon as PhUserSquare,
  UsersIcon as PhUsers,
  WaveSineIcon,
  type IconProps as PhosphorProps,
} from "@phosphor-icons/react";

// Phosphor icons under the app's icon names. Default size stays 14px as before.
type IconProps = PhosphorProps & { size?: number };
const wrap = (Glyph: ComponentType<PhosphorProps>) =>
  function AppIcon({ size = 14, ...props }: IconProps) {
    return <Glyph size={size} aria-hidden="true" {...props} />;
  };

export const SparkIcon = wrap(SparkleIcon);
export const ScanIcon = wrap(PhScan);
export const FileIcon = wrap(FileTextIcon);
export const HistoryIcon = wrap(ClockCounterClockwiseIcon);
export const UsersIcon = wrap(PhUsers);
export const CopyIcon = wrap(PhCopy);
export const DownloadIcon = wrap(DownloadSimpleIcon);
export const TrashIcon = wrap(PhTrash);
export const CheckIcon = wrap(PhCheck);
export const ArrowRightIcon = wrap(PhArrowRight);
export const EnterIcon = wrap(ArrowElbowDownLeftIcon);
export const CodeIcon = wrap(PhCode);
export const PaletteIcon = wrap(PhPalette);
export const GridIcon = wrap(SquaresFourIcon);
export const MotionIcon = wrap(PlayIcon);
export const AtomIcon = wrap(PhAtom);
export const TriangleIcon = wrap(PhTriangle);
export const WaveIcon = wrap(WaveSineIcon);
export const DiscIcon = wrap(PhDisc);
export const UserSquareIcon = wrap(PhUserSquare);
export const SearchIcon = wrap(MagnifyingGlassIcon);
export const CubeIcon = wrap(PhCube);
