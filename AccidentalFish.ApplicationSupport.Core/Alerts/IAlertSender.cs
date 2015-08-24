﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AccidentalFish.ApplicationSupport.Core.Alerts
{
    public interface IAlertSender
    {
        Task Send(string title, string message);
    }
}
