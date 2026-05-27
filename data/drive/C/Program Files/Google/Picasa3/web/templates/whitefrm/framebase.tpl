#templatefile 1.0 Lifescape web template file
# pound sign is a comment character
#
# lines that begin with "include" point to a file to be included at this point
# lines that begin with "loop" repeat the specified file once per image
# if loop or include files begin with "#templatefile" then that templatefile
# is processed in its entirety. In this way you can create 
# file-per-image exports. 
#
# The special "targetloop tplfile templatefile" command loops over each image, 
# interpreting tplfile for each one. A sequence number is appended to the export filename
# specified in tplfile. After the tplfiles have been exported, templatefile is included once per
# exported file, with "<%target%>" defined as the relative path of the exported file.
#
# The "copy" command copies the indicated path or directory to the export directory.
# Examples:
# copy everything from the "assets\" directory at the same level as the template into the output dir
# copy assets\ 
#
# copy a single file to the output dir
# copy c:\foo\bar.html 
#
# copy can also have a destination, so
# copy assets\ images\static\ 
# will create the directory "images\static\" in the output directory if necessary, then copy
# the contents of assets\ to it

# variables are substituted at inclusion time
define thumbnailWidth 120
define thumbnailHeight 120
define shadowedThumbnails 1
define noImageExport 1
define exportFileName index.html
include frameIndex.html
