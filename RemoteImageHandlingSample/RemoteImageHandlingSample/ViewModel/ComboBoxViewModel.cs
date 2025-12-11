using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace RemoteImageHandlingSample
{
    public class ComboBoxViewModel
    {
        /// <summary>
        /// Gets the collection of available image options for selection.
        /// </summary>
        public ObservableCollection<ImageOption> Images { get; }

        /// <summary>
        /// Gets or sets the currently selected image option.
        /// </summary>
        public ImageOption? SelectedOption { get; set; }

        /// <summary>
        /// Initializes a new instance of the ComboBoxViewModel class with a predefined collection of image options.
        /// </summary>
        public ComboBoxViewModel()
        {
            Images = new ObservableCollection<ImageOption>
            {
                new ImageOption
                {
                    Name = "High Resolution Images",
                    Urls = new[]{""}, // Can handle high resolution images by using valid urls.
                },
                new ImageOption
                {
                    Name = "PNG Format Images",
                    Urls = new[]
                    {
                         "https://cdn.pixabay.com/photo/2017/08/10/23/55/png-2629072_1280.png", 
                    }
                },
                new ImageOption
                {
                    Name = "RightTop Orientation Images",
                    Urls = new[]
                    {
                        "https://raw.githubusercontent.com/recurser/exif-orientation-examples/master/Landscape_1.jpg",
                    }
                },
                new ImageOption
                {
                    Name = "Large Size Images",
                    Urls = new[]
                    {
                        "https://placehold.co/4096x4096.png",
                    }
                }
            };

            SelectedOption = Images[0];
        }
    }

    /// <summary>
    /// Represents an image option with a name and a collection of associated URLs.
    /// </summary>
    public class ImageOption
    {
        /// <summary>
        /// Gets or sets the name associated with the object.
        /// </summary>
        public string Name { get; set; } = "";
        /// <summary>
        /// Gets or sets the collection of URLs associated with the instance.
        /// </summary>
        public string[] Urls { get; set; } = [];
        /// <summary>
        /// Returns a string that represents the current object, using its name.
        /// </summary>
        public override string ToString() => Name; // helpful for debugging
    }
}
