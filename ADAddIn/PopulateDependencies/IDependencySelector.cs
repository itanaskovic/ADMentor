﻿using AdAddIn.DataAccess;
using EAAddInFramework.DataAccess;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utils;

namespace AdAddIn.PopulateDependencies
{
    public interface IDependencySelector
    {
        Option<LabeledTree<ElementInstantiation, ModelEntity.Connector>> GetSelectedDependencies(
            LabeledTree<ElementInstantiation, ModelEntity.Connector> availableDependencies);
    }
}
