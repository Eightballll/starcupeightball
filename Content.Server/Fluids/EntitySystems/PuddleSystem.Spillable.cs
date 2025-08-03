using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.Fluids.Components;
using Content.Shared.Spillable;
using Content.Shared.Throwing;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player;
using Content.Shared._DV.Chemistry.Systems; // DeltaV Beergoggles enable safe throw
using Robust.Shared.Physics.Systems; // DeltaV Beergoggles enable safe throw

namespace Content.Server.Fluids.EntitySystems;

public sealed partial class PuddleSystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!; // DeltaV - Beergoggles enable safe throw
    [Dependency] private readonly SafeSolutionThrowerSystem _safesolthrower = default!; // DeltaV - Beergoggles enable safe throw

    protected override void InitializeSpillable()
    {
        base.InitializeSpillable();

        SubscribeLocalEvent<SpillableComponent, LandEvent>(SpillOnLand);
        // Openable handles the event if it's closed
        SubscribeLocalEvent<SpillableComponent, SolutionContainerOverflowEvent>(OnOverflow);
        SubscribeLocalEvent<SpillableComponent, SpillDoAfterEvent>(OnDoAfter);
    }

    private void OnOverflow(Entity<SpillableComponent> entity, ref SolutionContainerOverflowEvent args)
    {
        if (args.Handled)
            return;

        TrySpillAt(Transform(entity).Coordinates, args.Overflow, out _);
        args.Handled = true;
    }

    private void SpillOnLand(Entity<SpillableComponent> entity, ref LandEvent args)
    {
        if (!_solutionContainerSystem.TryGetSolution(entity.Owner, entity.Comp.SolutionName, out var soln, out var solution))
            return;

        if (Openable.IsClosed(entity.Owner))
            return;

        if (!entity.Comp.SpillWhenThrown)
            return;

        if (args.User != null)
        {
            // DeltaV - start of Beergoggles enable safe throw
            if (_safesolthrower.GetSafeThrow(args.User.Value))
            {
                _physics.SetAngularVelocity(entity, 0);
                Transform(entity).LocalRotation = Angle.Zero;
                return;
            }
            // DeltaV - end of Beergoggles enable safe throw
            _adminLogger.Add(LogType.Landed,
                $"{ToPrettyString(entity.Owner):entity} spilled a solution {SharedSolutionContainerSystem.ToPrettyString(solution):solution} on landing");
        }

        var drainedSolution = _solutionContainerSystem.Drain(entity.Owner, soln.Value, solution.Volume);
        TrySplashSpillAt(entity.Owner, Transform(entity).Coordinates, drainedSolution, out _);
    }

    /// <summary>
    /// Prevent Pacified entities from throwing items that can spill liquids.
    /// </summary>
    private void OnAttemptPacifiedThrow(Entity<SpillableComponent> ent, ref AttemptPacifiedThrowEvent args)
    {
        // Don’t care about closed containers.
        if (Openable.IsClosed(ent))
            return;

        // Don’t care about empty containers.
        if (!_solutionContainerSystem.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out _, out var solution) || solution.Volume <= 0)
            return;

        // DeltaV - start of Beergoggles enable safe throw
        if (_safesolthrower.GetSafeThrow(args.PlayerUid))
            return;
        // DeltaV - end of Beergoggles enable safe throw
        args.Cancel("pacified-cannot-throw-spill");
    }

    private void OnDoAfter(Entity<SpillableComponent> entity, ref SpillDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Args.Target == null)
            return;

        //solution gone by other means before doafter completes
        if (!_solutionContainerSystem.TryGetDrainableSolution(entity.Owner, out var soln, out var solution) || solution.Volume == 0)
            return;

        var puddleSolution = _solutionContainerSystem.SplitSolution(soln.Value, solution.Volume);
        TrySpillAt(Transform(entity).Coordinates, puddleSolution, out _);
        args.Handled = true;
    }
}
