﻿using ADMentor.ADTechnology;
using ADMentor.DataAccess;
using EAAddInBase;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EAAddInBase.Utils;

namespace ADMentor.UsabilityShortCuts
{
    /// <summary>
    /// Neglects all options of a given problem occurrence.
    /// </summary>
    sealed class NeglectAllOptionsCommand : ICommand<ProblemOccurrence, Unit>
    {
        private readonly AdRepository Repo;

        public NeglectAllOptionsCommand(AdRepository repo)
        {
            Repo = repo;
        }

        public Unit Execute(ProblemOccurrence po)
        {
            var alternatives = po.Alternatives(Repo.GetElement).Select(oo =>
            {
                oo.State = SolutionSpace.OptionState.Neglected;
                Repo.PropagateChanges(oo);
                return oo;
            });

            po.State = po.DeduceState(alternatives);
            Repo.PropagateChanges(po);

            return Unit.Instance;
        }

        public bool CanExecute(ProblemOccurrence _)
        {
            return true;
        }
    }
}
