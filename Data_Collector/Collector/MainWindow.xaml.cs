using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Collector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Info object stroing necessary information to show in table for imported text - contains ID, Content, Done
        private ObservableCollection<Info> info;
        // List of all fields' names of Info class
        private List<string> lst_Field_Names;
        // Path to save txt and wav files,which is the directory of current working project
        private string save_path_root = System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\Custom_Voice_Training_Data";
        private string save_path = "";
        private Info CurrentItem;

        public MainWindow()
        {
            // Initialize fields
            info = new ObservableCollection<Info>();
            lst_Field_Names = typeof(Info).GetProperties().Select(f => f.Name).ToList();

            // As usual
            InitializeComponent();

            // Initialize root folder to store folders for each set of training data
            if (!Directory.Exists(save_path_root))
            {
                //create a directory to save audios
                Directory.CreateDirectory(save_path_root);
            }

            // Some UI element initialization
            for (int i = 0; i < lst_Field_Names.Count; i++)
            {
                // Add columns according to types of fileds in Info class
                grdContent.Columns.Add(new GridViewColumn { Header = lst_Field_Names[i], DisplayMemberBinding = new Binding(lst_Field_Names[i]) });
            }
            Stop_Audio_Button.Visibility = Visibility.Collapsed;
            Stop_Audio_Button.IsEnabled = false;
            Terminate_Record_Button.Visibility = Visibility.Collapsed;
            Terminate_Record_Button.IsEnabled = false;
            Search_Type_ComboBox.ItemsSource = lst_Field_Names;
            Search_Type_ComboBox.SelectedIndex = lst_Field_Names.IndexOf("Content");
            AudioPlayer.LoadedBehavior = MediaState.Manual;
            AudioPlayer.Stop();
        }

        /// <summary>
        /// Select desired file and import data from it, then publish data to the table
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Import_Data(object sender, RoutedEventArgs e)
        {
            // UI elements adjustments
            Import_Button.IsEnabled = false;
            Import_Button.Content = "Start Another";

            // Open a dialog to select desired file - *.txt only
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Filter = "Text | *.txt";
            if (dialog.ShowDialog() == true)
            {
                info = Txt_To_Collection(dialog.FileName);
            }

            // Bind Info List as itemsource of the table
            lstContent.ItemsSource = info;

            // Set view for filtering
            CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lstContent.ItemsSource);

            // Set Filter for filtering on the fly
            view.Filter = UserFilter;

            // Initialize progress
            Update_Progress();

            // UI elements adjustment
            lstContent.Visibility = Visibility.Visible;
            Import_Button.IsEnabled = true;
        }

        /// <summary>
        /// Export collected data to zip files, a zip file called Data Package is the output
        /// Data Package has the following structure:
        /// Data Package - training_text_part_1.txt, audio_part_1,zip - 000000001.wav ... 000000009.wav
        ///              \ training_text_part_2.txt, audio_part_2,zip - 000000010.wav ... 000000019.wav
        ///                 .
        ///                 .
        ///                 .
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Export_Data(object sender, RoutedEventArgs e)
        {
            Export_Button.IsEnabled = false;

            // Check if any record exists
            bool flag = false;
            foreach (Info line_info in lstContent.Items)
            {
                if (line_info.Done == "Yes")
                    flag = true;
            }

            // If any record exist, pack them
            if (flag)
            {
                // Dialog windows to select place te save output
                System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog();
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    string output_root_path = dialog.SelectedPath;
                    // Folder that stores what is in "Data Package.zip", this one is used for compression's preparaion
                    string output_result_path = output_root_path + @"\Data Package";
                    // Compression path
                    string output_compress_path = output_root_path + @"\Data Package.zip";

                    // Create root directory for containing text files and zip files that contains wav files, this root directory will be compressed to output in the end
                    if (Directory.Exists(output_result_path))
                    {
                        Directory.Delete(output_result_path, true);
                    }

                    if (!Directory.Exists(output_result_path))
                    {
                        Directory.CreateDirectory(output_result_path);
                    }

                    // Create writer
                    StreamWriter writer = null;

                    // Variable for controling each zip file's size, no larger than 200M
                    long size_limit = 209715200;

                    // Size accumulator, total file size of current partition
                    long acc_size = 0;

                    // As its name
                    int partition_id = 1;

                    try
                    {
                        // Initialize writer
                        writer = new StreamWriter(output_result_path + @"\\" + partition_id.ToString() + ".txt");

                        // Directory to store some wav files
                        string partition_result_path = output_result_path + @"\" + partition_id.ToString();
                        // The compression path for previous folder
                        string partition_compression_path = output_result_path + @"\" + partition_id.ToString() + ".zip";

                        // Create folder partition_result_path
                        if (!Directory.Exists(partition_result_path))
                        {
                            Directory.CreateDirectory(partition_result_path);
                        }
                        
                        // Iterate through all entries in info
                        foreach (Info item in info)
                        {
                            // Only deal with those that has record
                            if (item.Done == "Yes")
                            {
                                // Get the entry's filename with and without path
                                string source_file_name = ID_To_FileName(item.ID);
                                string file_name = Path.GetFileName(source_file_name);

                                // Check the file's size (in bytes)
                                long size = new FileInfo(source_file_name).Length;

                                // Checking whether adding this new file into the current partition will explode the size limit
                                if (acc_size + size >= size_limit)
                                {
                                    writer.Close();

                                    // Compress the partition
                                    System.IO.Compression.ZipFile.CreateFromDirectory(partition_result_path, partition_compression_path);
                                    // Delete the folder that contains wav files
                                    Directory.Delete(partition_result_path, true);

                                    // Increment partition id
                                    partition_id++;

                                    // Reset size accumulator
                                    acc_size = 0;

                                    // Reset writer, partition folder and compress path
                                    writer = new StreamWriter(output_result_path + @"\\" + partition_id.ToString() + ".txt");
                                    partition_result_path = output_result_path + @"\" + partition_id.ToString();
                                    partition_compression_path = output_result_path + @"\" + partition_id.ToString() + ".zip";

                                    // Create partition folder
                                    if (!Directory.Exists(partition_result_path))
                                    {
                                        Directory.CreateDirectory(partition_result_path);
                                    }
                                }

                                // Increment acc_size by incoming file size
                                acc_size += new FileInfo(source_file_name).Length;
                                // Where to copy the original wav file to
                                string dest_file_name = partition_result_path + @"\" + file_name;
                                // Copy file
                                File.Copy(source_file_name, dest_file_name);
                                // Write the file's corresponding text content to text partition file
                                writer.WriteLine(file_name.Substring(0, (file_name.Length - 4)) + " " + item.Content);
                            }
                        }

                        // Last step, warp up the last partition
                        if (!File.Exists(partition_compression_path))
                        {
                            System.IO.Compression.ZipFile.CreateFromDirectory(partition_result_path, partition_compression_path);
                            Directory.Delete(partition_result_path, true);
                        }

                    }
                    finally
                    {
                        // Close it, for good.
                        if (writer != null)
                            writer.Close();

                        // Clear up old remains, and comprees to get output
                        if (File.Exists(output_compress_path))
                        {
                            File.Delete(output_compress_path);
                        }

                        if (!File.Exists(output_compress_path))
                        {
                            System.IO.Compression.ZipFile.CreateFromDirectory(output_result_path, output_compress_path);
                            Directory.Delete(output_result_path, true);
                        }

                        // Message after exportation is done
                        MessageBox.Show("Your output has been exported to " + output_compress_path + ". Please move or rename the output before exporting another project, or new outpt will overwrite the old one.");

                    }
                    
                }
            }
            // If not, show alert
            else
            {
                MessageBox.Show("There is no file to compress!");
            }

            Export_Button.IsEnabled = true;
        }

        /// <summary>
        /// Converting text file's content to a list of Info objects
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private ObservableCollection<Info> Txt_To_Collection(string path)
        {
            // Using the path's hash as the folder name
            save_path = save_path_root + "\\" + path.GetHashCode().ToString();
            if (!Directory.Exists(save_path))
            {
                // Create directory for this specific file
                Directory.CreateDirectory(save_path);

                // Create directory to store audios
                if (!Directory.Exists(save_path + @"\audio"))
                {
                    Directory.CreateDirectory(save_path + @"\audio");
                }
            }

            // Read selected file
            ObservableCollection<Info> result = new ObservableCollection<Info>();
            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            StreamReader file = new StreamReader(fs, System.Text.Encoding.Default);
            string line;
            int count = 0;
            while ((line = file.ReadLine()) != null)
            {
                // split the line by tab. if any
                string[] splitted_line = line.Split('\t');
                
                // Eliminate empty lines
                if (splitted_line[splitted_line.Length - 1] != "")
                {
                    // Increment count
                    count++;

                    // Initialize Done argument
                    string done = "No";
                    string file_name = ID_To_FileName(count.ToString()); // Get file name from count (equals ID)
                    if (File.Exists(file_name))
                    {
                        done = "Yes";
                    }

                    // Converting a line of text to one Info object
                    result.Add(new Info() { ID = count.ToString(), Content = splitted_line[splitted_line.Length - 1], Done = done });
                } 
            }

            return result;
        }

        /// <summary>
        /// Filter for textbox searching
        /// </summary>
        /// <param name="line_info"></param>
        /// <returns></returns>
        private bool UserFilter(object line_info)
        {
            if (String.IsNullOrEmpty(TextFilter_TextBox.Text))
            {
                return true;
            }
            else
            {
                return (line_info as Info).GetType().GetProperty(lst_Field_Names[Search_Type_ComboBox.SelectedIndex]).GetGetMethod().Invoke(line_info, null).ToString().IndexOf(TextFilter_TextBox.Text, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        /// <summary>   
        /// Call when new text enters textbox, refersh what is shown in the table
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void txtFilter_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(lstContent.ItemsSource).Refresh();
        }

        /// <summary>
        /// Select all entries
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Select_All(object sender, RoutedEventArgs e)
        {
            lstContent.SelectAll();
        }

        /// <summary>
        /// Delete selected entry(s) from the table
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Delete_Audios(object sender, RoutedEventArgs e)
        {
            if (lstContent.SelectedItems.Count == 0)
            {
                // Message for unselection notification
                MessageBox.Show(string.Format("Please select entries first."));
            }
            else
            {
                // For each recorded entry, delete its record
                foreach (Info item in lstContent.SelectedItems)
                {
                    // Get file name according to ID
                    string file_name = ID_To_FileName(item.ID);

                    // Delete audio file
                    if (File.Exists(file_name))
                    {
                        File.Delete(file_name);
                    }

                    info[Int32.Parse(item.ID) - 1].Done = "No";
                }

                // Refresh binding and view
                lstContent.ItemsSource = info;
                CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lstContent.ItemsSource);
                view.Filter = UserFilter;

                // Show message
                MessageBox.Show(string.Format("Audios of selected lines have been deleted."));
            }
            Update_Progress();
        }

        /// <summary>
        /// Delete the folder that contains all wav record files
        /// Wav files are also deleted
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Delete_All_Audios(object sender, RoutedEventArgs e)
        {
            // User double check
            System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Confirm to delete all wav files.", "Alert", System.Windows.Forms.MessageBoxButtons.YesNo);

            if (dialogResult == System.Windows.Forms.DialogResult.Yes)
            {
                if (Directory.Exists(save_path + @"\audio"))
                {
                    // Delete the all wav files in directory
                    DirectoryInfo dir = new DirectoryInfo(save_path + @"\audio");
                    foreach (FileInfo file in dir.EnumerateFiles())
                    {
                        file.Delete();
                    }

                    // Change Done status of all Info objects in info
                    foreach (Info item in info)
                    {
                        item.Done = "No";
                    }

                    // Bind Info List as itemsource of the table
                    lstContent.ItemsSource = info;

                    // Set view for filtering
                    CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lstContent.ItemsSource);

                    // Set Filter for filtering on the fly
                    view.Filter = UserFilter;

                    // Initialize progress
                    Update_Progress();
                }

                // Show completion message
                MessageBox.Show("All wav files cleared.");
            }
        }

        /// <summary>
        /// Audio recording frame
        /// </summary>
        /// <param name="lpstrCommand"></param>
        /// <param name="lpstrReturnString"></param>
        /// <param name="uReturnLength"></param>
        /// <param name="hwndCallback"></param>
        /// <returns></returns>
        [DllImport("winmm.dll", EntryPoint = "mciSendString", CharSet = CharSet.Auto)]
        public static extern int mciSendString(
                 string lpstrCommand,
                 string lpstrReturnString,
                 int uReturnLength,
                 int hwndCallback
                );

        /// <summary>
        /// Record audio for one or many selected audio
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartRecord(string file_name, string current_id)
        {
            if (File.Exists(file_name))
            {
                File.Delete(file_name);
            }
            Current_Content.Text = info[Int32.Parse(current_id) - 1].Content;
            Record_Button.Background = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
            Record_Button.Content = "Stop";

            mciSendString("set wave bitpersample 16", "", 0, 0);
            mciSendString("set wave samplespersec 20000", "", 0, 0);
            mciSendString("set wave channels 2", "", 0, 0);
            mciSendString("set wave format tag pcm", "", 0, 0);
            mciSendString("open new type WAVEAudio alias movie", "", 0, 0);
            mciSendString("record movie", "", 0, 0);
        }
        private void RecordNextAudio(Info current_item, Info next_item)
        {
            string file_name = ID_To_FileName(next_item.ID);
            StartRecord(file_name, next_item.ID);
            //lstContent.SelectedItems.IndexOf()
            CurrentItem = next_item;

        }
        private void Record_Audio(object sender, RoutedEventArgs e)
        {
            if (lstContent.SelectedIndex == -1)
            {
                MessageBox.Show("You have not selected anything yet!");
                return;
            }
            Play_Button.IsEnabled = false;
            if (CurrentItem == null)
            {
                CurrentItem = (Info)lstContent.SelectedItems[0];
            }
            string file_name = ID_To_FileName(CurrentItem.ID);
            if (Record_Button.Content.ToString() == "Record")
            {
                Current_Content.Text = CurrentItem.Content;
                Terminate_Record_Button.Visibility = Visibility.Visible;
                Terminate_Record_Button.IsEnabled = true;
                StartRecord(file_name, CurrentItem.ID);
            }
            else if (Record_Button.Content.ToString() == "Stop")
            {
                Record_Button.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
                Record_Button.Content = "Record";
                mciSendString("stop movie", "", 0, 0);


                mciSendString("save movie " + file_name, "", 0, 0);
                mciSendString("close movie", "", 0, 0);

                CurrentItem.Done = "Yes";

                Update_Progress();

                // Bind Info List as itemsource of the table
                lstContent.ItemsSource = info;

                // Set view for filtering
                CollectionView view = (CollectionView)CollectionViewSource.GetDefaultView(lstContent.ItemsSource);

                // Set Filter for filtering on the fly
                view.Filter = UserFilter;

                Info NextItem = null;
                if (lstContent.SelectedItems.IndexOf(CurrentItem) + 1 != lstContent.SelectedItems.Count)
                {
                    NextItem = (Info)lstContent.SelectedItems[lstContent.SelectedItems.IndexOf(CurrentItem) + 1];
                    if (CurrentItem != null && NextItem != null)
                        RecordNextAudio(CurrentItem, NextItem);
                }
                else
                {
                    CurrentItem = null;
                    Terminate_Record_Button.Visibility = Visibility.Collapsed;
                }
                    
            }
            Play_Button.IsEnabled = true;
        }

        /// <summary>
        /// MediaElement finish event
        /// When audio is finished, refresh play button
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Retrive current audio's ID
            string id = AudioSource_To_ID();

            // Get next ID that has a record
            string next_id = Next_Playable_ID(id);

            // Get next ID's file name
            string next_filename = ID_To_FileName(next_id);

            // Set new source
            AudioPlayer.Source = new Uri(next_filename);

            // UI element adjustment
            Current_Content.Text = info[Int32.Parse(next_id) - 1].Content;
        }

        /// <summary>
        /// Play record for selected entry(s)
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Play_Audios(object sender, RoutedEventArgs e)
        {
            // Show message when nothing is selected
            if (lstContent.SelectedIndex == -1)
            {
                MessageBox.Show("You have not selected anything yet!");
            }
            // Entries selected, ready to start
            else if (Play_Button.Content.ToString() == "Play")
            {

                string id = Next_Playable_ID("-1");

                if (id == "-1")
                {
                    MessageBox.Show("None of selected entries has a record.");
                }
                else
                {
                    if (AudioPlayer.Source == null)
                    {
                        string file_name = ID_To_FileName(id);
                        AudioPlayer.Source = new Uri(file_name);
                    }

                    Record_Button.IsEnabled = false;
                    Stop_Audio_Button.Visibility = Visibility.Visible;
                    Stop_Audio_Button.IsEnabled = true;

                    
                    AudioPlayer.LoadedBehavior = MediaState.Manual;
                    if(AudioPlayer.Source!=null)
                        AudioPlayer.Play();
                    Play_Button.Content = "Pause";
                }
            }
            else
            {
                AudioPlayer.Pause();
                Play_Button.Content = "Play";
            }
        }

        /// <summary>
        /// Stop audioplayer, unload audioplayer source
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Stop_Audio(object sender, RoutedEventArgs e)
        {
            // Unload source file
            AudioPlayer.Source = null;

            // Stop audio player
            AudioPlayer.Stop();

            // UI elements change
            Play_Button.Content = "Play";
            Stop_Audio_Button.Visibility = Visibility.Collapsed;
            Record_Button.IsEnabled = true;
        }

        /// <summary>
        /// Update complettion information, shows it in #completed/#total format
        /// </summary>
        private void Update_Progress()
        {
            int count = 0;

            // Traverse the Info (contains all information f) object once
            foreach (Info item in info)
            {
                if (item.Done == "Yes")
                {
                    count++;
                }
            }

            // Shown result in Progress textblock
            Progress.Text = "Progress: " + count.ToString() + "/" + info.Count.ToString();
        }

        /// <summary>
        /// Given a list of selecte entries (lstContent.SelectedItem), and an ID, return the smallest id with record
        /// If ID is "-1", the method returns the smallest ID that has a record, or return "-1" if no selected entry has a record
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        private string Next_Playable_ID(string ID)
        {
            // Retrive number of selected entries
            int length = lstContent.SelectedItems.Count;

            // When no entries is selected
            if (ID == "-1")
            {
                for (int i = 0; i < length; i++)
                {
                    Info item = (Info)lstContent.SelectedItems[i];
                    // Find the first ID with a record
                    if (ID == "-1")
                    {
                        if (item.Done == "Yes")
                        {
                            return item.ID;
                        }
                    }
                }

                // No selected entry has a record, return "-1"
                return "-1";
            }
            // Normal case, an ID (from 1 on) is given
            else
            {
                for (int i = 0; i < length; i++)
                {
                    Info item = (Info)lstContent.SelectedItems[i];
                    // Locate ID's corresponding item (an info object: ID-Content-Done)
                    if (ID == item.ID)
                    {
                        Info next_item;
                        // Rolling to next item that has a record, can roll back to the located item
                        while (true)
                        {
                            i = (i + 1) % length;
                            next_item = (Info)lstContent.SelectedItems[i];

                            // Will eventually be executed
                            if (next_item.Done == "Yes")
                            {
                                break;
                            }
                        }
                        return next_item.ID;
                    }
                }

                // Dummy return
                return ID;
            }
        }

        /// <summary>
        /// Format an ID to a wav file name, the result string contains path and file name
        /// </summary>
        /// <param name="ID"></param>
        /// <returns></returns>
        private string ID_To_FileName(string ID)
        {
            // file id shown in 9 digits, such as 000000001 or 000045364
            string file_id = ID.PadLeft(9, '0');
            // Compose file name
            string wav_name = string.Format(file_id + ".wav");
            // Compose file result string
            string file_name = string.Format(save_path + @"\audio\" + file_id + ".wav");

            return file_name;
        }

        /// <summary>
        /// Retive an ID (with no 0 padding) from an audio source
        /// </summary>
        /// <param name="file_name"></param>
        /// <returns></returns>
        private string AudioSource_To_ID()
        {
            // Retrive path of audioplayer's source
            string path = this.AudioPlayer.Source.LocalPath;

            // Retrive file name, without path
            string file_name = Path.GetFileName(path);

            // Retrive ID from file name, result in 9 digits, such as 000000001 or 000045364
            string ID = file_name.Substring(0, (file_name.Length - 4));

            // Trim leading 0s in ID
            ID = ID.TrimStart('0');

            return ID;
        }

        /// <summary>
        /// Rest some variables changed during record process
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Terminate_Record(object sender, RoutedEventArgs e)
        {
            // Clear memory
            CurrentItem = null;

            // UI elements adjustments
            Record_Button.Content = "Record";
            Record_Button.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
            Terminate_Record_Button.Visibility = Visibility.Collapsed;
            Play_Button.IsEnabled = true;
        }

        /// <summary>
        /// Show the selected content in the lower grid
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void lstContent_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Info item = (Info)lstContent.SelectedItems[0];
            Current_Content.Text = item.Content;
        }
    }

    /// <summary>
    /// Info class, containing three categories of information for one line of text from training text file
    /// </summary>
    public class Info
    {
        // From 1 on
        public string ID { get; set; }
        // Content written in text file - one line
        public string Content { get; set; }
        // Status of whether this line has been recorded yet
        public string Done { get; set; }
    }
}
