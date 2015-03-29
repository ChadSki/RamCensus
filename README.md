# RamCensus

Iterates memory usage by process (user-mode memory) as well as select kinds of kernel-mode memory.

* PagedPool -- Kernel-mode memory which can be swapped to disk, if necessary. RamCensus only counts the portion of the paged pool which is currently in physical RAM (not swapped to disk).
* NonPagedPool -- Kernel-mode memory which always remains in physical RAM.
* DriverCode -- Driver executables (but nothing dynamically allocated).
* SystemCache

## License

The entirety of my work on this project is released under the 2-clause BSD license.
While contributors retain copyright on their own work, I ask that pull requests also
be released under the 2-clause BSD license.
