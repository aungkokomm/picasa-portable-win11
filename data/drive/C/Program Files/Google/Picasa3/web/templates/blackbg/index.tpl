#templatefile -v "1.0" -n "Template 2, 2-page black background" -d "Thumbnail index page linked to a full view picture page."
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

####
# To override the values for width and height that the user has entered in the
# web export wizard, uncomment the imageWidth and imageHeight lines below
# define imageWidth 600
# define imageHeight 600
####
define bgColor #000000
define fgColor #ffffff
#define thumbnailWidth 80
#define thumbnailHeight 80
define shadowedThumbnails 1
define exportFileName index.html
include header.html
include imagelistheader.html
# the file indicated by the "loop" argument is processed once per image.
#loop imagelistelement.html
#include imagelistfooter.html
# if you have a targetloop command you probably shouldn't also have a loop command
# first argument to target loop is a template file to execute over each element. Second argument
# is for the local output pointing into the targets
include targetlistheader.html
targetloop imagetarget.tpl includedtarget.html
include footer.html
copy assets\