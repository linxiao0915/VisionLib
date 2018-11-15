﻿using GalaSoft.MvvmLight.Command;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace JPT_TosaTest.UserCtrl.VisionDebugTool
{
    /// <summary>
    /// Interaction logic for UC_Flag.xaml
    /// </summary>
    public partial class UC_FlagPanel : UserControl , INotifyPropertyChanged
    {
        public UC_FlagPanel()
        {
            InitializeComponent();
        }
        public ObservableCollection<string> LineList
        {
            get
            {
                return GetValue(LineListProperty) as ObservableCollection<string>;
            }
            set
            {
                SetValue(LineListProperty, value);
            }

        }
        public static readonly DependencyProperty LineListProperty = DependencyProperty.Register("LineList", typeof(ObservableCollection<string>), typeof(UC_FlagPanel));

        public RelayCommand<string> SaveParaCommand
        {
            get
            {
                return GetValue(SaveParaCommandProperty) as RelayCommand<string>;
            }
            set
            {
                SetValue(SaveParaCommandProperty, value);
            }

        }
        public static readonly DependencyProperty SaveParaCommandProperty = DependencyProperty.Register("SaveParaCommand", typeof(RelayCommand<string>), typeof(UC_FlagPanel));

        public object SaveCommandParameter
        {
            get
            {
                return GetValue(SaveCommandParameterProperty);
            }
            set
            {
                SetValue(SaveCommandParameterProperty, value);
            }

        }
        public static readonly DependencyProperty SaveCommandParameterProperty = DependencyProperty.Register("SaveCommandParameter", typeof(object), typeof(UC_FlagPanel));

        public RelayCommand<string> UpdateParaCommand
        {
            get
            {
                return GetValue(UpdateParaCommandProperty) as RelayCommand<string>;
            }
            set
            {
                SetValue(UpdateParaCommandProperty, value);
            }

        }
        public static readonly DependencyProperty UpdateParaCommandProperty = DependencyProperty.Register("UpdateParaCommand", typeof(RelayCommand<string>), typeof(UC_FlagPanel));

        public object UpdateCommandParameter
        {
            get
            {
                return GetValue(UpdateCommandParameterProperty) as object;
            }
            set
            {
                SetValue(UpdateCommandParameterProperty, value);
            }

        }
        public static readonly DependencyProperty UpdateCommandParameterProperty = DependencyProperty.Register("UpdateCommandParameter", typeof(object), typeof(UC_FlagPanel));



        public string Data
        {
            get { return $"FlagTool|{CbFlagType.Text}&{cbLine1.Text}&{cbLine2.Text}"; }
        }

        private void BtnSavePara_Click(object sender, RoutedEventArgs e)
        {
            if(SaveParaCommand!=null)
                SaveParaCommand.Execute(SaveCommandParameter);
        }
        private void ExcuteUpdateCommand()
        {
            if (UpdateParaCommand != null)
            {
                RaisePropertyChanged("Data");
                UpdateParaCommand.Execute(UpdateCommandParameter);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName]string PropertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(PropertyName));
        }
    }
}